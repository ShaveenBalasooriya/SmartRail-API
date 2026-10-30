using SmartRail.Host.IntegrationTests.Flows.Steps;
using SmartRail.Host.IntegrationTests.Flows.Support;
using SmartRail.Host.IntegrationTests.Infrastructure;
using SmartRail.SharedKernel.Application.Abstractions;

namespace SmartRail.Host.IntegrationTests.Flows;

/// <summary>
/// Flow 4, separation of duties, at flow level: on every route that publishes a change to a journey,
/// a Guard's token and an Auditor's token are refused with 403, the refusal leaves the journey's
/// changes, events, outbox rows and public detail untouched, exactly one denied audit entry names the
/// actor, role, action and time, an unauthenticated or unusable token is refused with 401 and is not
/// recorded at all, an Auditor's read is allowed and silent, the chain still verifies afterwards — and
/// the controller succeeds on the very same routes, so none of the refusals can pass vacuously.
/// </summary>
/// <remarks>
/// Every test builds its own journey (and, where the route needs one, its own departure or published
/// change) through the real generation, Guard and controller endpoints, and removes it again. The
/// audit record is append-only and shared, so a denial is looked up by the announcement it was written
/// from and silence is measured from the chain position the flow started at — never by a global count —
/// and the entries the flow appended are removed again when it finishes, because the neighbouring
/// suites identify a denial by its actor and action and must not see an older one of the flow's. The
/// neighbouring <c>StaffAuthorizationTests</c> already proves the policy endpoint by endpoint; what
/// this flow adds is the per-route proof on the real publishing routes, with the state and the record
/// checked behind it.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class SeparationOfDutiesFlowTests : IAsyncLifetime
{
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

    [Theory]
    [InlineData(ControllerWrite.Delay)]
    [InlineData(ControllerWrite.Cancellation)]
    [InlineData(ControllerWrite.PlatformChange)]
    [InlineData(ControllerWrite.Termination)]
    [InlineData(ControllerWrite.Correction)]
    public async Task A_guard_token_is_refused_on_every_controller_write(ControllerWrite write)
    {
        var target = await _flow.Writes.ArrangeAsync(write);

        var attempt = await _flow.Writes.AttemptAsync(write, StaffDirectory.Guard, target);

        Verify.Refused(
            attempt,
            $"A Guard's POST {ControllerWriteSteps.Name(write)} should be refused");
    }

    [Theory]
    [InlineData(ControllerWrite.Delay)]
    [InlineData(ControllerWrite.Cancellation)]
    [InlineData(ControllerWrite.PlatformChange)]
    [InlineData(ControllerWrite.Termination)]
    [InlineData(ControllerWrite.Correction)]
    public async Task An_auditor_token_is_refused_on_every_controller_write(ControllerWrite write)
    {
        var target = await _flow.Writes.ArrangeAsync(write);

        var attempt = await _flow.Writes.AttemptAsync(write, StaffDirectory.Auditor, target);

        Verify.Refused(
            attempt,
            $"An Auditor's POST {ControllerWriteSteps.Name(write)} should be refused");
    }

    [Theory]
    [InlineData(ControllerWrite.Delay)]
    [InlineData(ControllerWrite.Cancellation)]
    [InlineData(ControllerWrite.PlatformChange)]
    [InlineData(ControllerWrite.Termination)]
    [InlineData(ControllerWrite.Correction)]
    public async Task A_refused_write_leaves_the_journeys_changes_events_and_outbox_rows_untouched(
        ControllerWrite write)
    {
        var name = ControllerWriteSteps.Name(write);
        var target = await _flow.Writes.ArrangeAsync(write);
        var before = await _flow.Internals.SnapshotAsync(target.Journey.Id);
        var baseline = await _flow.Identity.MaxPositionAsync();

        await _flow.Writes.AttemptAsync(write, StaffDirectory.Guard, target);

        // The refusal is recorded asynchronously, so wait for it to be fully processed: a late write
        // by the denied attempt would have happened by now, and the comparison below would catch it.
        await _flow.Identity.WaitUntilDeliveredAsync(
            baseline,
            $"the Guard's refused {name} should be announced and delivered to the audit consumer");

        var after = await _flow.Internals.SnapshotAsync(target.Journey.Id);

        Verify.Unchanged(
            before.Render(),
            after.Render(),
            $"The journey's published changes, journey events and outbox rows should be untouched by a "
            + $"Guard's refused {name}");
    }

    [Theory]
    [InlineData(ControllerWrite.Delay)]
    [InlineData(ControllerWrite.Cancellation)]
    [InlineData(ControllerWrite.PlatformChange)]
    [InlineData(ControllerWrite.Termination)]
    [InlineData(ControllerWrite.Correction)]
    public async Task A_refused_write_leaves_the_public_journey_detail_unchanged(ControllerWrite write)
    {
        var name = ControllerWriteSteps.Name(write);
        var target = await _flow.Writes.ArrangeAsync(write);

        var before = await _flow.Public.JourneyAsync(target.Journey.Id);
        Verify.Ok(before, $"the public journey detail should be readable before a Guard's refused {name}");

        await _flow.Writes.AttemptAsync(write, StaffDirectory.Guard, target);

        var after = await _flow.Public.JourneyAsync(target.Journey.Id);
        Verify.Ok(after, $"the public journey detail should still be readable after a Guard's refused {name}");

        Verify.Unchanged(
            before.Value!.RawJson,
            after.Value!.RawJson,
            $"The public journey detail should be unchanged by a Guard's refused {name}");
    }

    [Theory]
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
    public async Task A_denied_write_is_recorded_once_naming_the_actor_role_action_and_time(
        string role,
        ControllerWrite write)
    {
        var session = SessionFor(role);
        var name = ControllerWriteSteps.Name(write);
        var target = await _flow.Writes.ArrangeAsync(write);
        var baseline = await _flow.Identity.MaxPositionAsync();

        await _flow.Writes.AttemptAsync(write, session, target);

        var announcement = await _flow.Identity.WaitForSingleDenialAsync(
            baseline,
            $"a {RoleName(role)}'s refused {name}");

        var entry = await _flow.Audit.WaitForDenialAsync(
            announcement,
            $"the audit entry for a {RoleName(role)}'s refused {name}");

        Verify.Equal(
            "denied",
            entry.Outcome,
            $"The entry for a {RoleName(role)}'s refused {name} should be recorded as denied");
        Verify.Equal<Guid?>(
            session.StaffId,
            entry.ActorStaffId,
            $"The entry for a {RoleName(role)}'s refused {name} should name that staff member as its actor");
        Verify.Equal(
            session.Role,
            entry.ActorRole,
            $"The entry for a {RoleName(role)}'s refused {name} should name the role it was attempted in");
        Verify.Equal(
            ControllerWriteSteps.Action(write),
            entry.Action,
            $"The entry for a {RoleName(role)}'s refused {name} should name the action that was attempted");
        Verify.Equal(
            _flow.Now,
            entry.OccurredAt,
            $"The entry for a {RoleName(role)}'s refused {name} should carry the server time of the refusal");
    }

    [Fact]
    public async Task The_auditor_can_read_the_audit_list_and_reading_records_no_denial()
    {
        var baseline = await _flow.Audit.MaxChainPositionAsync();

        var read = await _flow.Auditor.ListAsync();
        Verify.Ok(read, "The Auditor should be able to read the audit list");

        await _flow.Audit.NothingRecordedAfterAsync(
            baseline,
            "Reading the audit list should not be recorded as a denied attempt");
    }

    [Fact]
    public async Task A_controller_write_with_no_token_is_refused_and_not_recorded_as_a_denial()
    {
        var target = await _flow.Writes.ArrangeAsync(ControllerWrite.Delay);
        var baseline = await _flow.Audit.MaxChainPositionAsync();

        var attempt = await _flow.Writes.AttemptDelayAsync(session: null, target);

        Verify.Unauthorized(attempt, "A controller write with no token should be refused");
        await _flow.Audit.NothingRecordedAfterAsync(
            baseline,
            "An unauthenticated attempt should not be recorded as a denied attempt");
    }

    [Fact]
    public async Task An_expired_controller_token_is_refused()
    {
        var target = await _flow.Writes.ArrangeAsync(ControllerWrite.Delay);

        var attempt = await _flow.Writes.AttemptDelayAsync(
            StaffDirectory.ControllerWithExpiredToken,
            target);

        Verify.Unauthorized(attempt, "A controller write with an expired token should be refused");
    }

    [Fact]
    public async Task A_controller_token_signed_with_the_wrong_key_is_refused()
    {
        var target = await _flow.Writes.ArrangeAsync(ControllerWrite.Delay);

        var attempt = await _flow.Writes.AttemptDelayAsync(
            StaffDirectory.ControllerWithForeignKey,
            target);

        Verify.Unauthorized(attempt, "A controller write with a token signed by an untrusted key should be refused");
    }

    [Fact]
    public async Task The_audit_chain_verifies_after_every_denied_write()
    {
        var baseline = await _flow.Identity.MaxPositionAsync();

        foreach (var write in ControllerWriteSteps.All)
        {
            var target = await _flow.Writes.ArrangeAsync(write);

            await _flow.Writes.AttemptAsync(write, StaffDirectory.Guard, target);
        }

        var announcements = await _flow.Identity.WaitForDenialsAsync(
            baseline,
            ControllerWriteSteps.All.Count,
            "Every one of the flow's denied writes");

        foreach (var announcement in announcements)
        {
            await _flow.Audit.WaitForDenialAsync(
                announcement,
                "every one of the flow's denied writes");
        }

        var report = await _flow.Auditor.IntegrityAsync();
        Verify.Ok(report, "The Auditor should be able to check the audit chain");

        Verify.Equal(
            "intact",
            report.Value!.Status,
            "The audit chain should still verify after every denied write");
    }

    [Theory]
    [InlineData(ControllerWrite.Delay)]
    [InlineData(ControllerWrite.Cancellation)]
    [InlineData(ControllerWrite.PlatformChange)]
    [InlineData(ControllerWrite.Termination)]
    [InlineData(ControllerWrite.Correction)]
    public async Task The_controller_can_publish_every_controller_write(ControllerWrite write)
    {
        var target = await _flow.Writes.ArrangeAsync(write);

        var attempt = await _flow.Writes.AttemptAsync(write, StaffDirectory.Controller, target);

        Verify.Created(
            attempt,
            $"The controller's POST {ControllerWriteSteps.Name(write)}, which the refusals are measured against,");
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
