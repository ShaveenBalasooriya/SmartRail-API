using Microsoft.Extensions.Logging;
using SmartRail.LiveOperations.Domain.LiveState;
using SmartRail.RailNetwork.Contracts;
using SmartRail.SharedKernel.Application.Events;

namespace SmartRail.LiveOperations.Application.LiveState.CreateLiveState;

/// <summary>
/// Creates a journey's live state when Rail Network announces it (design D4, D9). The repository's
/// inserts are idempotent, so the start-up backfill that repairs a lost announcement cannot collide
/// with this consumer, and a redelivered announcement creates nothing.
/// </summary>
internal sealed class JourneyGeneratedHandler(
    ILiveStateRepository repository,
    TimeProvider timeProvider,
    ILogger<JourneyGeneratedHandler> logger) : IEventConsumer<JourneyGenerated>
{
    public string Name => "live-operations.journey-generated";

    public async Task Handle(JourneyGenerated integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var now = timeProvider.GetUtcNow();
        var baseline = LiveStateBaseline.From(integrationEvent, now);
        var created = JourneyLiveState.Create(baseline, integrationEvent.ServiceId, integrationEvent.LineId, now);

        if (created.IsFailure)
        {
            // The backfill repairs a journey whose announcement could not be turned into state.
            logger.LogWarning(
                "Live state for journey {JourneyId} was not created: {ErrorCode}",
                integrationEvent.JourneyId,
                created.Error.Code);
            return;
        }

        await repository.CreateIfMissingAsync(created.Value, cancellationToken);
    }
}
