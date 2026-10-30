using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SmartRail.LiveOperations.Application.Abstractions;
using SmartRail.LiveOperations.Contracts;
using SmartRail.LiveOperations.Domain.Calculation;
using SmartRail.LiveOperations.Domain.LiveState;
using SmartRail.SharedKernel.Application.Events;
using SmartRail.SharedKernel.Application.Messaging;
using SmartRail.SharedKernel.Results;
using JourneyEventType = SmartRail.LiveOperations.Contracts.JourneyEventType;

namespace SmartRail.LiveOperations.Application.LiveState.ApplyJourneyFact;

/// <summary>
/// Applies one recorded fact to a journey's live state (design D4). It replaces the old
/// <c>IJourneyFactSink</c>: in one transaction it locks the journey's row, recomputes the state,
/// writes it and appends the journey event. A published change always appends its event, because the
/// channels that receive it apply events by id and sequence; every other fact appends one only when
/// the state changed. The <see cref="JourneyStateChanged"/> announcement is recorded in the module's
/// outbox in that same transaction, so it commits or rolls back with the journey event (design D1,
/// D9).
/// </summary>
/// <param name="JourneyId">The journey the fact belongs to.</param>
/// <param name="Fact">
/// The fact as the calculator sees it, ordered by the order it was committed in, or
/// <see langword="null"/> when the fact is already stored and the reader supplies it — a published
/// change is written to <c>status_changes</c> before this command runs, so it needs no second copy.
/// </param>
/// <param name="EventType">Which of the event types this change is.</param>
/// <param name="SourceId">The departure log or status change that recorded the fact, when there is one.</param>
/// <param name="OccurredAt">When the fact happened, for example the device time of a departure.</param>
/// <param name="RecordedAt">When the server recorded the fact.</param>
public sealed record ApplyJourneyFactCommand(
    Guid JourneyId,
    Fact? Fact,
    JourneyEventType EventType,
    Guid? SourceId,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt) : ICommand;

internal sealed class ApplyJourneyFactCommandHandler(
    ILiveStateRepository repository,
    IJourneyFactReader factReader,
    IUnitOfWork unitOfWork,
    [FromKeyedServices(IntegrationEventPublisherKeys.Operations)] IIntegrationEventPublisher publisher,
    TimeProvider timeProvider) : ICommandHandler<ApplyJourneyFactCommand>
{
    public async Task<Result> Handle(
        ApplyJourneyFactCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            var liveState = await repository.GetForUpdateAsync(command.JourneyId, cancellationToken);
            if (liveState is null)
            {
                return Result.Failure(JourneyLiveStateErrors.NotFound(command.JourneyId));
            }

            var facts = (await factReader.GetFactsAsync(command.JourneyId, cancellationToken)).ToList();
            if (command.Fact is { } fact && !facts.Contains(fact))
            {
                // A reader that does not see the fact yet must not make the fact a no-op.
                facts.Add(fact);
            }

            var changed = liveState.Apply(facts, timeProvider.GetUtcNow());

            // The publish time of a correction is kept in the live state, because the public view has
            // to show that the journey was corrected and until now only the change row knew it
            // (decision D-10). It is set before the snapshot below is taken, so the correction's
            // event carries the same time the journey reports afterwards. No other event type
            // touches it.
            if (command.EventType == JourneyEventType.CorrectionPublished)
            {
                changed |= liveState.MarkCorrected(command.OccurredAt);
            }

            // A published change always appends its event, even when the recomputation left the
            // state as it was: correcting an earlier platform change while a later one is in force
            // changes nothing visible, but every channel reached by the original still has to
            // receive the correction (change-correction spec, PAX-12, CAT-09). Any other fact keeps
            // the "only when the state changed" rule, so a replayed departure log consumes no
            // sequence number and appends no event (journey-event-stream spec).
            if (changed || IsPublishedChange(command.EventType))
            {
                // The snapshot is the public LiveJourneyView, which the Domain cannot reference, so
                // the handler serialises it here with the same options the event queries read with.
                var snapshot = JsonSerializer.Serialize(
                    LiveStateMapping.ToView(liveState),
                    LiveJsonOptions.Default);

                var appended = liveState.RecordEvent(
                    LiveStateMapping.ToDomain(command.EventType),
                    command.SourceId,
                    command.OccurredAt,
                    command.RecordedAt,
                    snapshot);

                repository.Append(appended);

                // The stream position is assigned by the database when the event is inserted, so it
                // is known only after this save. The announcement follows it, in the same
                // transaction, so a rollback still publishes nothing and a committed change still
                // carries its position for a stream client to resume from (design D1, D6, D9).
                await unitOfWork.SaveChangesAsync(cancellationToken);

                // The outbox row carries the journey event's ID, with the journey ID as its ordering
                // key, and commits or rolls back with the event (design D1, D6, D9).
                publisher.Publish(
                    new JourneyStateChanged(
                        appended.JourneyId,
                        appended.Id,
                        appended.SequenceNumber,
                        appended.StreamPosition,
                        LiveStateMapping.ToContract(appended.EventType)),
                    appended.Id,
                    appended.JourneyId.ToString());

                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            // A fact that changes nothing and is not a published change consumes no sequence number
            // and appends no event; the transaction still commits so the row lock is released.
            await transaction.CommitAsync(cancellationToken);
        }

        return Result.Success();
    }

    /// <summary>
    /// Whether the fact is one of the five published changes, each of which produces exactly one
    /// journey event and one <see cref="JourneyStateChanged"/> announcement however the state
    /// recomputes (<c>docs/data-model.md</c>: every status change produces one event).
    /// </summary>
    private static bool IsPublishedChange(JourneyEventType eventType) =>
        eventType is JourneyEventType.DelayPublished
            or JourneyEventType.CancellationPublished
            or JourneyEventType.TerminationPublished
            or JourneyEventType.PlatformChanged
            or JourneyEventType.CorrectionPublished;
}
