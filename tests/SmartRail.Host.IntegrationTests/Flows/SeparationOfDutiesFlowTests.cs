using SmartRail.Host.IntegrationTests.Flows.Steps;
using SmartRail.Host.IntegrationTests.Flows.Support;
using SmartRail.Host.IntegrationTests.Infrastructure;
using SmartRail.SharedKernel.Application.Abstractions;

namespace SmartRail.Host.IntegrationTests.Flows;

/// <summary>
/// Flow 4, separation of duties, at flow level: the controller publishes a change on the very routes a
/// Guard's and an Auditor's token are refused on, so the refusals cannot pass vacuously; each refusal is
/// a 403 problem-details answer and is recorded once in the audit record, naming the actor, the role, the
/// action and the server time; and a refused write leaves the controller's view of the journey and the
/// journey's events exactly as they were.
/// </summary>
/// <remarks>
/// <para>
/// Every test builds its own journey (and, where the route needs one, its own departure or published
/// change) through the real generation, Guard and controller endpoints. The audit record is append-only
/// and shared, so a denial is found by the baseline of denied entries this flow took itself and the actor
/// the attempt was made as — never by a global count — and the entries the flow appended are removed
/// again when it finishes.
/// </para>
/// <para>
/// The neighbouring <c>StaffAuthorizationTests</c> already proves the policy endpoint by endpoint; what
/// this flow adds is the per-route proof on the real publishing routes, with the record read behind it
/// through the Auditor's API.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class SeparationOfDutiesFlowTests : IAsyncLifetime
{
    private const int DelayMinutes = 15;

    private readonly PostgresFixture _postgres;
    private FlowContext _flow = null!;

    public SeparationOfDutiesFlowTests(PostgresFixture postgres) => _postgres = postgres;

    /// <inheritdoc />
    public Task InitializeAsync()
    {
        _flow = new FlowContext(_postgres);
        return _flow.InitializeAsync();
    }

    /// <inheritdoc />
    public Task DisposeAsync() => _flow.DisposeAsync();

    [FlowFact]
    public async Task Happy_path_the_controller_publishes_a_delay_and_no_denial_is_recorded()
    {
        // Arrange
        var journey = await _flow.Journeys.CreateAsync();
        var deniedBefore = await _flow.Auditor.DeniedEntryIdsAsync(StaffDirectory.ControllerId);

        // Act — the controller makes the same write a Guard's and an Auditor's token is refused on.
        var published = await _flow.ControllerWrites(StaffDirectory.Controller)
            .PublishDelayAsync(journey.Id, fromStopSequence: 2, minutes: 5);

        // Assert
        Verify.Created(
            published,
            "The controller's delay on the route the refusals are measured against,");

        await _flow.Auditor.NothingDeniedAfterAsync(
            StaffDirectory.ControllerId,
            deniedBefore,
            "the controller's accepted delay should not be recorded as a denied attempt");
    }

    [FlowTheory]
    [InlineData(Roles.Guard, ControllerWrite.Delay)]
    [InlineData(Roles.Guard, ControllerWrite.Cancellation)]
    [InlineData(Roles.Guard, ControllerWrite.PlatformChange)]
    [InlineData(Roles.Guard, ControllerWrite.Termination)]
    [InlineData(Roles.Guard, ControllerWrite.Correction)]
    [InlineData(Roles.Auditor, ControllerWrite.Delay)]
    [InlineData(Roles.Auditor, ControllerWrite.Cancellation)]
    [InlineData(Roles.Auditor, ControllerWrite.PlatformChange)]
    [InlineData(Roles.Auditor, ControllerWrite.Termination)]
    [InlineData(Roles.Auditor, ControllerWrite.Correction)]
    public async Task A_guard_or_an_auditor_token_is_refused_on_every_controller_write(
        string role,
        ControllerWrite write)
    {
        // Arrange
        var target = await _flow.Writes.ArrangeAsync(write);

        // Act
        var attempt = await _flow.Writes.AttemptAsync(write, SessionFor(role), target);

        // Assert
        Verify.Refused(
            attempt,
            $"A {RoleName(role)}'s POST {ControllerWriteSteps.Name(write)} should be refused");
    }

    [FlowTheory]
    [InlineData(Roles.Guard)]
    [InlineData(Roles.Auditor)]
    public async Task A_denied_write_is_recorded_once_naming_the_actor_role_action_and_time(string role)
    {
        // Arrange
        var session = SessionFor(role);
        var target = await _flow.Writes.ArrangeAsync(ControllerWrite.Delay);
        var baseline = await _flow.Auditor.DeniedEntryIdsAsync(session.StaffId);

        // Act
        var attempt = await _flow.Writes.AttemptAsync(ControllerWrite.Delay, session, target);

        // Assert
        Verify.Refused(attempt, $"A {RoleName(role)}'s delay on a journey should be refused");

        var entry = await _flow.Auditor.WaitForNewDenialAsync(
            session.StaffId,
            "PublishDelay",
            baseline,
            $"a {RoleName(role)}'s refused delay should be recorded once as a denied entry");

        Verify.Equal("denied", entry.Outcome, $"The entry for a {RoleName(role)}'s refused delay should be denied");
        Verify.Equal(
            session.StaffId,
            entry.ActorStaffId,
            $"The entry for a {RoleName(role)}'s refused delay should name that staff member as its actor");
        Verify.Equal(
            session.Role,
            entry.ActorRole,
            $"The entry for a {RoleName(role)}'s refused delay should name the role it was attempted in");
        Verify.Equal(
            "PublishDelay",
            entry.Action,
            $"The entry for a {RoleName(role)}'s refused delay should name the action that was attempted");
        Verify.Equal(
            _flow.Now,
            entry.OccurredAt,
            $"The entry for a {RoleName(role)}'s refused delay should carry the server time of the refusal");

        await _flow.Auditor.OnlyOneDenialAppearsAsync(
            session.StaffId,
            baseline,
            $"a {RoleName(role)}'s refused delay should be recorded once and not again");
    }

    [FlowFact]
    public async Task A_refused_write_leaves_the_controllers_view_and_the_journeys_events_unchanged()
    {
        // Arrange — a running journey, whose departure is the event a client has already received.
        var journey = await _flow.Journeys.CreateAsync(stopCount: 4);
        var lastSeen = await RunningDeparturePositionAsync(journey);

        var controller = _flow.ControllerWrites(StaffDirectory.Controller);
        var before = await controller.JourneyViewAsync(journey.Id);
        Verify.Ok(before, "The controller's view should be readable before the refused write");

        // Act
        var refused = await _flow.ControllerWrites(StaffDirectory.Guard)
            .PublishDelayAsync(journey.Id, fromStopSequence: 3, DelayMinutes);

        // Assert — the controller's view of the journey, its stops and its changes is unchanged.
        Verify.Refused(refused, "A Guard's delay on a journey should be refused");

        var after = await controller.JourneyViewAsync(journey.Id);
        Verify.Ok(after, "The controller's view should still be readable after the refused write");
        Verify.Unchanged(
            before.Value!.Render(),
            after.Value!.Render(),
            "A Guard's refused delay should leave the controller's view of the journey unchanged");

        // Assert — and no event reached the live channel, which a reconnecting client would have seen.
        using var resumed = await _flow.Public.ResumeStreamAsync(journey.Id, lastSeen);
        var missed = await resumed.ReadAsync(TimeSpan.FromSeconds(2), stopAtFirstEvent: false);

        Verify.Equal(
            0,
            missed.Events.Count,
            "A Guard's refused delay should append no event a reconnecting client could catch up on");
    }

    /// <summary>
    /// Makes the journey running through a real Guard departure and returns the stream position of the
    /// frame that departure produced: the point a client has read up to, taken from the stream itself.
    /// </summary>
    private async Task<string> RunningDeparturePositionAsync(Builders.Journey journey)
    {
        string lastSeen;

        using (var live = await _flow.Public.OpenStreamAsync(journey.Id))
        {
            await _flow.Changes.MakeRunningAsync(journey);

            var progress = await live.ReadAsync(Eventually.Timeout, stopAtFirstEvent: true);

            Verify.Equal(
                1,
                progress.Events.Count,
                "The public stream should deliver the departure that makes the journey running");
            Verify.Equal(
                "departure_logged",
                progress.Events[0].EventType,
                "The public stream should deliver the running journey's departure as a departure_logged event");
            Verify.True(
                progress.Events[0].Id is not null,
                "The departure's frame should carry the stream position a client resumes from");

            lastSeen = progress.Events[0].Id!;
        }

        return lastSeen;
    }

    /// <summary>The staff session a role named in a theory case acts as.</summary>
    private static StaffSession SessionFor(string role) => role switch
    {
        Roles.Guard => StaffDirectory.Guard,
        Roles.Auditor => StaffDirectory.Auditor,
        _ => throw new ArgumentOutOfRangeException(
            nameof(role),
            role,
            "The separation-of-duties flow covers the Guard and the Auditor."),
    };

    /// <summary>The role as a sentence reads it, for a failure message.</summary>
    private static string RoleName(string role) => role switch
    {
        Roles.Guard => "Guard",
        Roles.Auditor => "Auditor",
        _ => role,
    };
}
