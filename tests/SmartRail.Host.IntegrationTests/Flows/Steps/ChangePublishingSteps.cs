using SmartRail.Host.IntegrationTests.Flows.Builders;
using SmartRail.Host.IntegrationTests.Flows.Drivers;
using SmartRail.Host.IntegrationTests.Flows.Support;

namespace SmartRail.Host.IntegrationTests.Flows.Steps;

/// <summary>
/// The flow-step layer for the changes a controller publishes. A test says which change it is about
/// and what the journey has to look like first; this step knows the route behind it and checks that
/// the setup itself succeeded, so a flow that fails later is never failing because its arrangement
/// quietly answered a refusal. Every publish here is the controller's own successful write.
/// </summary>
internal sealed class ChangePublishingSteps
{
    private readonly ControllerDriver _controller;
    private readonly GuardDriver _guard;
    private readonly DateTimeOffset _now;

    public ChangePublishingSteps(HttpClient client, DateTimeOffset now)
    {
        _now = now;
        _controller = new ControllerDriver(client, StaffDirectory.Controller);
        _guard = new GuardDriver(client, StaffDirectory.Guard);
    }

    /// <summary>
    /// Makes a journey running by logging a real departure at its first calling stop, which is what
    /// a delay or a termination on a running journey needs. The device time defaults to just before
    /// the frozen clock, so the departure is plausible and the stop is still on its station's board.
    /// </summary>
    public async Task MakeRunningAsync(Journey journey, DateTimeOffset? departedAt = null)
    {
        ArgumentNullException.ThrowIfNull(journey);

        var attempt = await _guard.LogDepartureAsync(
            journey.Id,
            stopSequence: 1,
            departedAt ?? _now.AddMinutes(-1));

        Verify.Created(
            attempt,
            $"the Guard's departure at stop 1, which makes journey {journey.Id} running,");
    }

    /// <summary>Publishes a delay and returns the change the API recorded.</summary>
    public async Task<ControllerChange> DelayAsync(Journey journey, int fromStopSequence, int minutes)
    {
        ArgumentNullException.ThrowIfNull(journey);

        var attempt = await _controller.PublishDelayAsync(journey.Id, fromStopSequence, minutes);

        Verify.Created(attempt, $"the controller's delay from stop {fromStopSequence} of journey {journey.Id},");

        return attempt.Value!;
    }

    /// <summary>Publishes a platform change and returns the change the API recorded.</summary>
    public async Task<ControllerChange> PlatformChangeAsync(Journey journey, int stopSequence, Guid platformId)
    {
        ArgumentNullException.ThrowIfNull(journey);

        var attempt = await _controller.PublishPlatformChangeAsync(journey.Id, stopSequence, platformId);

        Verify.Created(
            attempt,
            $"the controller's platform change at stop {stopSequence} of journey {journey.Id},");

        return attempt.Value!;
    }

    /// <summary>Cancels a journey and returns the change the API recorded.</summary>
    public async Task<ControllerChange> CancelAsync(Journey journey)
    {
        ArgumentNullException.ThrowIfNull(journey);

        var attempt = await _controller.PublishCancellationAsync(journey.Id);

        Verify.Created(attempt, $"the controller's cancellation of journey {journey.Id},");

        return attempt.Value!;
    }

    /// <summary>Corrects a published change and returns the correction the API recorded.</summary>
    public async Task<ControllerChange> CorrectAsync(Guid changeId)
    {
        var attempt = await _controller.PublishCorrectionAsync(changeId);

        Verify.Created(attempt, $"the controller's correction of change {changeId},");

        return attempt.Value!;
    }
}
