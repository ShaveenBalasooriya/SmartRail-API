using System.Text.Json;
using SmartRail.LiveOperations.Application.LiveState;
using SmartRail.LiveOperations.Contracts;
using SmartRail.LiveOperations.Domain.LiveState;
using SmartRail.SharedKernel.Application.Messaging;
using SmartRail.SharedKernel.Results;
using DomainJourneyEventType = SmartRail.LiveOperations.Domain.LiveState.JourneyEventType;

namespace SmartRail.LiveOperations.Application.JourneyEvents.GetEventsAfter;

/// <summary>
/// The events after a stream position, in position order, optionally restricted to one journey.
/// <paramref name="Limit"/> is clamped to 1..500 so a catch-up can be paged.
/// </summary>
public sealed record GetEventsAfterQuery(long Position, Guid? JourneyId = null, int Limit = 100)
    : IQuery<IReadOnlyList<JourneyEventView>>;

internal sealed class GetEventsAfterQueryHandler(IJourneyEventRepository repository)
    : IQueryHandler<GetEventsAfterQuery, IReadOnlyList<JourneyEventView>>
{
    /// <summary>The largest batch a catch-up may ask for in one call.</summary>
    private const int MaxEventLimit = 500;

    public async Task<Result<IReadOnlyList<JourneyEventView>>> Handle(
        GetEventsAfterQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var events = await repository.GetAfterAsync(
            query.Position,
            query.JourneyId,
            Math.Clamp(query.Limit, 1, MaxEventLimit),
            cancellationToken);

        // A correction names the original change and the event that recorded it, so a channel that
        // received the original can match the correction to it (change-correction spec). The links
        // are read from the stored rows rather than from a column on the event itself.
        var correctionChangeIds = events
            .Where(journeyEvent => journeyEvent.EventType == DomainJourneyEventType.CorrectionPublished
                && journeyEvent.StatusChangeId is not null)
            .Select(journeyEvent => journeyEvent.StatusChangeId!.Value)
            .Distinct()
            .ToArray();

        var corrections = correctionChangeIds.Length == 0
            ? new Dictionary<Guid, CorrectionReference>()
            : await repository.GetCorrectionReferencesAsync(correctionChangeIds, cancellationToken);

        return Result.Success<IReadOnlyList<JourneyEventView>>(
            [.. events.Select(journeyEvent => ToView(journeyEvent, corrections))]);
    }

    private static JourneyEventView ToView(
        JourneyEvent journeyEvent,
        IReadOnlyDictionary<Guid, CorrectionReference> corrections)
    {
        var correction = journeyEvent.EventType == DomainJourneyEventType.CorrectionPublished
            && journeyEvent.StatusChangeId is { } changeId
            && corrections.TryGetValue(changeId, out var reference)
                ? reference
                : null;

        return new JourneyEventView(
            journeyEvent.Id,
            LiveStateMapping.ToContract(journeyEvent.EventType),
            journeyEvent.JourneyId,
            journeyEvent.SequenceNumber,
            journeyEvent.StreamPosition,
            journeyEvent.StatusChangeId ?? journeyEvent.DepartureLogId,
            journeyEvent.OccurredAt,
            journeyEvent.RecordedAt,
            JsonSerializer.Deserialize<LiveJourneyView>(journeyEvent.Payload, LiveJsonOptions.Default)
                ?? throw new InvalidOperationException(
                    $"Journey event {journeyEvent.Id} carries a payload that is not a live journey snapshot."),
            correction?.CorrectsChangeId,
            correction?.CorrectsEventId);
    }
}
