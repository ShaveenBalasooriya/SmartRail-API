using MediatR;
using Microsoft.Extensions.Logging;
using SmartRail.LiveOperations.Domain.LiveState;
using SmartRail.RailNetwork.Contracts;

namespace SmartRail.LiveOperations.Application.LiveState.CreateLiveState;

/// <summary>
/// Creates a journey's live state when Rail Network announces it (design D4). The repository's
/// inserts are idempotent, so the start-up backfill that repairs a lost announcement cannot collide
/// with this handler.
/// </summary>
internal sealed class JourneyGeneratedHandler(
    ILiveStateRepository repository,
    TimeProvider timeProvider,
    ILogger<JourneyGeneratedHandler> logger) : INotificationHandler<JourneyGenerated>
{
    public async Task Handle(JourneyGenerated notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var now = timeProvider.GetUtcNow();
        var baseline = LiveStateBaseline.From(notification, now);
        var created = JourneyLiveState.Create(baseline, notification.ServiceId, notification.LineId, now);

        if (created.IsFailure)
        {
            // The backfill repairs a journey whose announcement could not be turned into state.
            logger.LogWarning(
                "Live state for journey {JourneyId} was not created: {ErrorCode}",
                notification.JourneyId,
                created.Error.Code);
            return;
        }

        await repository.CreateIfMissingAsync(created.Value, cancellationToken);
    }
}
