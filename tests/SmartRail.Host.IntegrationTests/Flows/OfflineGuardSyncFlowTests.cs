using System.Net;
using SmartRail.Host.IntegrationTests.Flows.Drivers;
using SmartRail.Host.IntegrationTests.Flows.Support;
using SmartRail.Host.IntegrationTests.Infrastructure;

namespace SmartRail.Host.IntegrationTests.Flows;

/// <summary>
/// Flow 2, the offline Guard's log syncing exactly once, at flow level: two queued departures reach
/// the server after the fact and each records the stop's actual departure at the device time of the
/// tap, not the time the queue managed to send it; replaying the same queue answers 200 and writes
/// nothing again — no second departure, no second journey event, no second outbox row — so the
/// passenger app, the station display and the stream are not told twice; the public detail and the
/// public stream show the progress; a log out of time order, a stop the journey does not have, another
/// guard's journey and a request with no credential are each refused by the rule that owns them,
/// leaving the journey's progress as it was.
/// </summary>
/// <remarks>
/// Every test builds its own journey through the real generation announcement and logs through the
/// real sync endpoint, with the Host on a frozen clock at noon of the railway day: a device time
/// twenty minutes earlier is still today's, so the queue is a real post-midnight catch-up rather than
/// a date-window case, and the receipt time is exact. The flow removes the journey, its rows and the
/// audit entries it appended, as the neighbouring suites leave the shared record.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class OfflineGuardSyncFlowTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private FlowContext _flow = null!;

    public OfflineGuardSyncFlowTests(PostgresFixture postgres) => _postgres = postgres;

    /// <inheritdoc />
    public Task InitializeAsync()
    {
        _flow = new FlowContext(_postgres);
        return _flow.InitializeAsync();
    }

    /// <inheritdoc />
    public Task DisposeAsync() => _flow.DisposeAsync();

    [Fact]
    public async Task Two_queued_logs_each_record_one_departure_at_the_device_time_they_were_tapped()
    {
        var journey = await _flow.Journeys.CreateAsync(stopCount: 4);
        var firstTap = _flow.Now.AddMinutes(-20);
        var secondTap = _flow.Now.AddMinutes(-19);

        var first = await _flow.Guard.LogDepartureAsync(journey.Id, stopSequence: 1, firstTap);
        Verify.Created(first, "The first queued log at stop 1, synced after the fact,");

        var second = await _flow.Guard.LogDepartureAsync(journey.Id, stopSequence: 2, secondTap);
        Verify.Created(second, "The second queued log at stop 2, synced after the fact,");

        Verify.Equal(
            firstTap,
            first.Value!.DepartedAt,
            "The sync should answer the device time the Guard tapped at stop 1 with, not the receipt time");
        Verify.Equal(
            _flow.Now,
            first.Value.ReceivedAt,
            "The sync should record the server's receipt time beside the device time");

        var departures = await _flow.Internals.DeparturesAsync(journey.Id);

        Verify.Equal(2, departures.Count, "Two queued logs should leave exactly one departure log each");
        Verify.Equal(firstTap, departures[0].DepartedAt, "Stop 1's stored departure should be its device time");
        Verify.Equal(secondTap, departures[1].DepartedAt, "Stop 2's stored departure should be its device time");
        Verify.Equal(
            firstTap,
            await _flow.Internals.ActualDepartureAsync(journey.Id, 1),
            "The live state should record stop 1 as departed at the device time, not the sync time");
        Verify.Equal(
            secondTap,
            await _flow.Internals.ActualDepartureAsync(journey.Id, 2),
            "The live state should record stop 2 as departed at the device time, not the sync time");
    }

    [Fact]
    public async Task Replaying_the_queued_logs_answers_ok_and_creates_nothing_again()
    {
        var journey = await _flow.Journeys.CreateAsync(stopCount: 4);
        var firstTap = _flow.Now.AddMinutes(-20);
        var secondTap = _flow.Now.AddMinutes(-19);
        var firstId = Guid.CreateVersion7();
        var secondId = Guid.CreateVersion7();

        Verify.Created(
            await _flow.Guard.LogDepartureAsync(journey.Id, 1, firstTap, id: firstId),
            "The Guard's first queued log, which the replay repeats,");
        Verify.Created(
            await _flow.Guard.LogDepartureAsync(journey.Id, 2, secondTap, id: secondId),
            "The Guard's second queued log, which the replay repeats,");

        var before = await _flow.Internals.SnapshotAsync(journey.Id);

        var firstReplay = await _flow.Guard.LogDepartureAsync(journey.Id, 1, firstTap, id: firstId);
        var secondReplay = await _flow.Guard.LogDepartureAsync(journey.Id, 2, secondTap, id: secondId);

        Verify.Ok(firstReplay, "Replaying the queued log for stop 1 should be answered 200");
        Verify.Ok(secondReplay, "Replaying the queued log for stop 2 should be answered 200");

        var after = await _flow.Internals.SnapshotAsync(journey.Id);

        Verify.Unchanged(
            before.Render(),
            after.Render(),
            "Replaying two queued logs should not create a second departure, journey event or outbox row");
        Verify.Equal(
            2,
            (await _flow.Internals.DeparturesAsync(journey.Id)).Count,
            "Replaying two queued logs should leave exactly the two departure logs the first sync wrote");
    }

    [Fact]
    public async Task The_public_journey_detail_shows_the_last_logged_stop_as_last_passed()
    {
        var journey = await _flow.Journeys.CreateAsync(stopCount: 4);
        var firstTap = _flow.Now.AddMinutes(-20);
        var secondTap = _flow.Now.AddMinutes(-19);

        Verify.Created(
            await _flow.Guard.LogDepartureAsync(journey.Id, 1, firstTap),
            "The Guard's queued log at stop 1, which the detail should show as passed,");
        Verify.Created(
            await _flow.Guard.LogDepartureAsync(journey.Id, 2, secondTap),
            "The Guard's queued log at stop 2, which the detail should show as last passed,");

        var detail = await _flow.Public.JourneyViewAsync(journey.Id);
        Verify.Ok(detail, "The public journey detail should be readable after the queue synced");

        Verify.Equal(
            SeededNetwork.Kollupitiya,
            detail.Value!.LastDepartedStation?.Id,
            "The public journey detail should name stop 2's station as the last one the train passed");
        Verify.Equal(
            secondTap,
            detail.Value.Stop(2).ActualDeparture,
            "The public journey detail should show stop 2's actual departure at its device time");
        Verify.Equal(
            "running",
            detail.Value.Status,
            "The public journey detail should show a journey that has left its first stop as running");
    }

    [Fact]
    public async Task The_public_stream_delivers_progress_for_a_new_log_and_not_for_its_replay()
    {
        var journey = await _flow.Journeys.CreateAsync();
        var tap = _flow.Now.AddMinutes(-20);
        var logId = Guid.CreateVersion7();

        using var stream = await _flow.Public.OpenStreamAsync(journey.Id);

        Verify.Created(
            await _flow.Guard.LogDepartureAsync(journey.Id, 1, tap, id: logId),
            "The Guard's queued log at stop 1, which the passenger stream should deliver,");

        var delivered = await stream.ReadAsync(Eventually.Timeout, stopAtFirstEvent: true);

        Verify.Equal(
            1,
            delivered.Events.Count,
            "The public stream should deliver the synced departure as exactly one progress event");
        Verify.Equal(
            "departure_logged",
            delivered.Events[0].EventType,
            "The public stream should deliver the synced departure as a departure event");

        using (var payload = System.Text.Json.JsonDocument.Parse(delivered.Events[0].Data))
        {
            Verify.Equal(
                journey.Id,
                payload.RootElement.GetProperty("journeyId").GetGuid(),
                "The public stream's progress event should carry the journey whose stop departed");
        }

        // The queue retries blindly, so the same entry is sent again: the stream must stay silent.
        Verify.Ok(
            await _flow.Guard.LogDepartureAsync(journey.Id, 1, tap, id: logId),
            "Replaying the delivered queued log should be answered 200");

        var quiet = await stream.ReadAsync(TimeSpan.FromSeconds(2), stopAtFirstEvent: false);

        Verify.Equal(
            0,
            quiet.Events.Count,
            "The public stream should not deliver a replayed departure a second time");
    }

    [Fact]
    public async Task A_device_time_that_contradicts_a_later_stop_that_already_synced_is_refused()
    {
        var journey = await _flow.Journeys.CreateAsync(stopCount: 4);
        var laterStopTap = _flow.Now.AddMinutes(-20);
        var earlierStopTap = _flow.Now.AddMinutes(-19);

        // The queue reached the server in reverse stop order, which on its own is not a contradiction:
        // the device times, not the arrival order, say when each stop departed.
        Verify.Created(
            await _flow.Guard.LogDepartureAsync(journey.Id, 2, laterStopTap),
            "The Guard's queued log at stop 2, which the queue delivered before stop 1's,");

        var contradiction = await _flow.Guard.LogDepartureAsync(journey.Id, 1, earlierStopTap);

        Verify.BadRequest(
            contradiction,
            "A queued log for stop 1 whose device time is after stop 2's already synced departure should be refused");

        Verify.Equal<DateTimeOffset?>(
            null,
            await _flow.Internals.ActualDepartureAsync(journey.Id, 1),
            "The refused contradictory log should leave stop 1 without a recorded departure");
        Verify.Equal(
            laterStopTap,
            await _flow.Internals.ActualDepartureAsync(journey.Id, 2),
            "The refused contradictory log should leave stop 2's progress as it was");
        Verify.Equal(
            1,
            (await _flow.Internals.DeparturesAsync(journey.Id)).Count,
            "The refused contradictory log should leave exactly the one departure the journey had");
    }

    [Fact]
    public async Task A_log_for_a_stop_the_journey_does_not_have_is_refused()
    {
        var journey = await _flow.Journeys.CreateAsync();

        var unknownStop = await _flow.Guard.LogDepartureAsync(
            journey.Id,
            stopSequence: 9,
            _flow.Now.AddMinutes(-20));

        Verify.BadRequest(
            unknownStop,
            "A queued log for a stop sequence the journey does not have should be refused");

        Verify.Empty(
            await _flow.Internals.DeparturesAsync(journey.Id),
            "The refused log should leave the journey with no departure at all");
    }

    [Fact]
    public async Task A_log_for_another_guards_journey_is_refused_and_changes_nothing()
    {
        var journey = await _flow.Journeys.CreateAsync(guardStaffId: StaffDirectory.SecondGuard.StaffId);
        var before = await _flow.Internals.SnapshotAsync(journey.Id);
        var baseline = await _flow.Identity.MaxPositionAsync();

        var refused = await _flow.Guard.LogDepartureAsync(
            journey.Id,
            stopSequence: 1,
            _flow.Now.AddMinutes(-20));

        Verify.Refused(
            refused,
            "A Guard's log for a journey assigned to another guard should be refused");

        // The refusal is announced asynchronously: waiting for its delivery means nothing the denied
        // attempt could have written is still on its way when the state is compared.
        await _flow.Identity.WaitUntilDeliveredAsync(
            baseline,
            "the other guard's refused departure log should be announced and delivered to the audit consumer");

        var after = await _flow.Internals.SnapshotAsync(journey.Id);

        Verify.Unchanged(
            before.Render(),
            after.Render(),
            "A Guard's refused log for another guard's journey should change nothing about the journey");
        Verify.Empty(
            await _flow.Internals.DeparturesAsync(journey.Id),
            "A Guard's refused log for another guard's journey should record no departure");
    }

    [Fact]
    public async Task A_log_with_no_token_is_refused()
    {
        var journey = await _flow.Journeys.CreateAsync();
        var anonymousGuard = new GuardDriver(_flow.Api, session: null);

        var attempt = await anonymousGuard.LogDepartureAsync(
            journey.Id,
            stopSequence: 1,
            _flow.Now.AddMinutes(-20));

        Verify.Unauthorized(attempt, "A departure log sent with no credential should be refused");

        Verify.Empty(
            await _flow.Internals.DeparturesAsync(journey.Id),
            "A departure log sent with no credential should record no departure");
    }
}
