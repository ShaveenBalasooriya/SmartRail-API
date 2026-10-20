using System.Text.Json;
using SmartRail.LiveOperations.Application.LiveState;
using SmartRail.LiveOperations.Contracts;
using SmartRail.LiveOperations.Domain.LiveState;
using SmartRail.SharedKernel.Application.Messaging;
using SmartRail.SharedKernel.Results;

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

        return Result.Success<IReadOnlyList<JourneyEventView>>([.. events.Select(ToView)]);
    }

    private static JourneyEventView ToView(JourneyEvent journeyEvent)
    {
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
                    $"Journey event {journeyEvent.Id} carries a payload that is not a live journey snapshot."));
    }
}
