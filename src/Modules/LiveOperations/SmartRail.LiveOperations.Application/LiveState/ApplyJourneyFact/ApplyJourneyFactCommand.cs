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
/// writes it and appends one event only when the state changed. The <see cref="JourneyStateChanged"/>
/// announcement is recorded in the module's outbox in that same transaction, so it commits or rolls
/// back with the journey event (design D1, D9).
/// </summary>
/// <param name="JourneyId">The journey the fact belongs to.</param>
/// <param name="Fact">The fact as the calculator sees it, ordered by the order it was committed in.</param>
/// <param name="EventType">Which of the six event types this change is.</param>
/// <param name="SourceId">The departure log or status change that recorded the fact, when there is one.</param>
/// <param name="OccurredAt">When the fact happened, for example the device time of a departure.</param>
/// <param name="RecordedAt">When the server recorded the fact.</param>
public sealed record ApplyJourneyFactCommand(
    Guid JourneyId,
    Fact Fact,
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
            if (!facts.Contains(command.Fact))
            {
                // A reader that does not see the fact yet must not make the fact a no-op.
                facts.Add(command.Fact);
            }

            var changed = liveState.Apply(facts, timeProvider.GetUtcNow());

            if (changed)
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

                // The outbox row joins the journey event in the same SaveChangesAsync, so a
                // rollback publishes nothing and the event carries the journey event's ID with the
                // journey ID as its ordering key (design D1, D6, D9).
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

            // A fact that changes nothing consumes no sequence number, appends no event and still
            // commits so the row lock is released.
            await transaction.CommitAsync(cancellationToken);
        }

        return Result.Success();
    }
}
