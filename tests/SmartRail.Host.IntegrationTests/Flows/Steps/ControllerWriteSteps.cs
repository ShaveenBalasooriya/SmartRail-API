using SmartRail.Host.IntegrationTests.Flows.Builders;
using SmartRail.Host.IntegrationTests.Flows.Drivers;
using SmartRail.Host.IntegrationTests.Flows.Support;

namespace SmartRail.Host.IntegrationTests.Flows.Steps;

/// <summary>One of the five routes that publish a change to a journey, named by what it does.</summary>
public enum ControllerWrite
{
    /// <summary>A delay from a calling stop that has not departed.</summary>
    Delay,

    /// <summary>A cancellation of a journey that has no recorded departure.</summary>
    Cancellation,

    /// <summary>A platform change of a calling stop that has not departed.</summary>
    PlatformChange,

    /// <summary>An early termination of a running journey.</summary>
    Termination,

    /// <summary>A correction that withdraws a change published today.</summary>
    Correction,
}

/// <summary>What a controller write is aimed at: the flow's journey, and the change a correction names.</summary>
/// <param name="Journey">The journey the write is attempted on.</param>
/// <param name="ChangeId">The change a correction withdraws, when the write is a correction.</param>
internal sealed record ControllerWriteTarget(Journey Journey, Guid? ChangeId);

/// <summary>
/// The flow-step layer for the Network Controller's writes. A test says which write it is about; this
/// step knows the route behind it, what the journey has to look like for the write to be a real attempt
/// rather than a 404 or a 400, and what the attempt answered. Everything a test would otherwise have to
/// repeat — URLs, bodies, the departure that makes a journey running, the delay a correction withdraws —
/// lives here, so the tests contain intent only.
/// </summary>
internal sealed class ControllerWriteSteps
{
    private readonly HttpClient _client;
    private readonly JourneyBuilder _journeys;
    private readonly DateTimeOffset _now;

    public ControllerWriteSteps(HttpClient client, JourneyBuilder journeys, DateTimeOffset now)
    {
        _client = client;
        _journeys = journeys;
        _now = now;
    }

    /// <summary>Every controller write, in the order the flow covers them.</summary>
    public static IReadOnlyList<ControllerWrite> All { get; } =
    [
        ControllerWrite.Delay,
        ControllerWrite.Cancellation,
        ControllerWrite.PlatformChange,
        ControllerWrite.Termination,
        ControllerWrite.Correction,
    ];

    /// <summary>The write's name as a sentence reads it, for a failure message.</summary>
    public static string Name(ControllerWrite write) => write switch
    {
        ControllerWrite.Delay => "delay",
        ControllerWrite.Cancellation => "cancellation",
        ControllerWrite.PlatformChange => "platform change",
        ControllerWrite.Termination => "termination",
        ControllerWrite.Correction => "correction",
        _ => throw new ArgumentOutOfRangeException(nameof(write), write, "Unknown controller write."),
    };

    /// <summary>The endpoint name the audit record stores for the write.</summary>
    public static string Action(ControllerWrite write) => write switch
    {
        ControllerWrite.Delay => "PublishDelay",
        ControllerWrite.Cancellation => "PublishCancellation",
        ControllerWrite.PlatformChange => "PublishPlatformChange",
        ControllerWrite.Termination => "PublishTermination",
        ControllerWrite.Correction => "PublishCorrection",
        _ => throw new ArgumentOutOfRangeException(nameof(write), write, "Unknown controller write."),
    };

    /// <summary>
    /// Creates the flow's own journey and puts it in the state the write applies to, so an attempt by
    /// the wrong role is refused by the access rule and not by a rule about the journey. For a
    /// termination that means a real departure at stop 1, logged by the Guard; for a correction it
    /// means a delay the controller published, which the correction then withdraws.
    /// </summary>
    public async Task<ControllerWriteTarget> ArrangeAsync(ControllerWrite write)
    {
        var journey = await _journeys.CreateAsync();

        switch (write)
        {
            case ControllerWrite.Termination:
            {
                var departure = await new GuardDriver(_client, StaffDirectory.Guard).LogDepartureAsync(
                    journey.Id,
                    stopSequence: 1,
                    departedAt: _now);

                Verify.Created(
                    departure,
                    "the Guard's departure at stop 1, which makes the journey running for a termination,");

                break;
            }

            case ControllerWrite.Correction:
            {
                var published = await new ControllerDriver(_client, StaffDirectory.Controller)
                    .PublishDelayAsync(journey.Id, fromStopSequence: 2, minutes: 5);

                Verify.Created(
                    published,
                    "the controller's delay, which the correction the flow attempts has to withdraw,");

                return new ControllerWriteTarget(journey, published.Value!.Id);
            }

            default:
                break;
        }

        return new ControllerWriteTarget(journey, ChangeId: null);
    }

    /// <summary>
    /// Attempts the write as the given staff session and returns what the server answered. A
    /// <see langword="null"/> session sends no credential at all.
    /// </summary>
    public Task<ApiAttempt<ControllerChange>> AttemptAsync(
        ControllerWrite write,
        StaffSession? session,
        ControllerWriteTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var controller = new ControllerDriver(_client, session);

        return write switch
        {
            ControllerWrite.Delay => controller.PublishDelayAsync(
                target.Journey.Id,
                fromStopSequence: 2,
                minutes: 5),

            ControllerWrite.Cancellation => controller.PublishCancellationAsync(target.Journey.Id),

            ControllerWrite.PlatformChange => controller.PublishPlatformChangeAsync(
                target.Journey.Id,
                stopSequence: 1,
                platformId: SeededNetwork.ColomboFortPlatformThree),

            ControllerWrite.Termination => controller.PublishTerminationAsync(
                target.Journey.Id,
                lastStopSequence: 2),

            ControllerWrite.Correction => controller.PublishCorrectionAsync(target.ChangeId!.Value),

            _ => throw new ArgumentOutOfRangeException(nameof(write), write, "Unknown controller write."),
        };
    }

    /// <summary>Attempts a delay, the write the authentication cases use as their representative.</summary>
    public Task<ApiAttempt<ControllerChange>> AttemptDelayAsync(StaffSession? session, ControllerWriteTarget target) =>
        AttemptAsync(ControllerWrite.Delay, session, target);
}
