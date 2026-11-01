using SmartRail.Host.IntegrationTests.Flows.Support;
using SmartRail.Host.IntegrationTests.Infrastructure;

namespace SmartRail.Host.IntegrationTests.Flows;

/// <summary>
/// Flow 2, the offline Guard's log syncing exactly once, at flow level: two queued departures reach the
/// server after the fact and each records the stop's actual departure at the device time of the tap, not
/// the time the queue managed to send it; the passenger detail shows the progress, the live stream
/// delivers each synced departure once, and replaying the queue answers 200 and writes nothing again —
/// so the passenger app, the station display and the stream are not told twice. The negatives are the
/// story's own: a device time that contradicts a later stop that already synced is refused, and a Guard's
/// log for another Guard's journey is refused (403), each leaving the journey as it was.
/// </summary>
/// <remarks>
/// Every test builds its own journey through the real generation announcement and logs through the real
/// sync endpoint, with the Host on a frozen clock at noon of the railway day: a device time twenty
/// minutes earlier is still today's, so the queue is a real post-midnight catch-up rather than a
/// date-window case, and the receipt time is exact. Progress is read where a passenger or the Guard
/// would see it — the public detail, the public stream and the log's own answer.
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

    [FlowFact]
    public async Task Happy_path_two_queued_departures_sync_once_at_their_device_times_and_a_replay_writes_nothing()
    {
        // Arrange
        var journey = await _flow.Journeys.CreateAsync(stopCount: 4);
        var firstTap = _flow.Now.AddMinutes(-20);
        var secondTap = _flow.Now.AddMinutes(-19);
        var firstId = Guid.CreateVersion7();
        var secondId = Guid.CreateVersion7();

        using var stream = await _flow.Public.OpenStreamAsync(journey.Id);

        // Act
        var first = await _flow.Guard.LogDepartureAsync(journey.Id, stopSequence: 1, firstTap, id: firstId);
        var second = await _flow.Guard.LogDepartureAsync(journey.Id, stopSequence: 2, secondTap, id: secondId);

        // Assert — each log keeps the button-press time, with the server's receipt time beside it.
        Verify.Created(first, "The first queued log at stop 1, synced after the fact,");
        Verify.Created(second, "The second queued log at stop 2, synced after the fact,");

        Verify.Equal(
            firstTap,
            first.Value!.DepartedAt,
            "The sync should answer the device time the Guard tapped at stop 1 with, not the receipt time");
        Verify.Equal(
            _flow.Now,
            first.Value.ReceivedAt,
            "The sync should record the server's receipt time beside the device time");
        Verify.Equal(
            secondTap,
            second.Value!.DepartedAt,
            "The sync should answer the device time the Guard tapped at stop 2 with");

        // Assert — the passenger detail shows the progress the queue synced.
        var detail = await _flow.Public.JourneyViewAsync(journey.Id);
        Verify.Ok(detail, "The public journey detail should be readable after the queue synced");

        Verify.Equal(
            SeededNetwork.Kollupitiya,
            detail.Value!.LastDepartedStation?.Id,
            "The public journey detail should name stop 2's station as the last one the train passed");
        Verify.Equal(
            firstTap,
            detail.Value.Stop(1).ActualDeparture,
            "The public journey detail should show stop 1's actual departure at its device time");
        Verify.Equal(
            secondTap,
            detail.Value.Stop(2).ActualDeparture,
            "The public journey detail should show stop 2's actual departure at its device time");
        Verify.Equal(
            "running",
            detail.Value.Status,
            "The public journey detail should show a journey that has left its first stop as running");

        var beforeReplay = await _flow.Public.JourneyAsync(journey.Id);
        Verify.Ok(beforeReplay, "The public journey detail should be readable before the replay");

        // Assert — the live channel delivered each synced departure once.
        var delivered = await stream.ReadAsync(
            Eventually.Timeout,
            stopAtFirstEvent: false,
            stopAfterEvents: 2);

        Verify.Equal(
            2,
            delivered.Events.Count,
            "The public stream should deliver the two synced departures as exactly two progress events");
        Verify.Equal(
            "departure_logged, departure_logged",
            string.Join(", ", delivered.Events.Select(frame => frame.EventType)),
            "The public stream should deliver each synced departure as a departure event");

        // Act — the queue retries blindly, so it sends both entries again with the same identifiers.
        var firstReplay = await _flow.Guard.LogDepartureAsync(journey.Id, 1, firstTap, id: firstId);
        var secondReplay = await _flow.Guard.LogDepartureAsync(journey.Id, 2, secondTap, id: secondId);

        // Assert — the replay is accepted as the same log, and nothing on the journey changed.
        Verify.Ok(firstReplay, "Replaying the queued log for stop 1 should be answered 200");
        Verify.Ok(secondReplay, "Replaying the queued log for stop 2 should be answered 200");
        Verify.Equal(
            firstId,
            firstReplay.Value!.Id,
            "The replay should answer the log the first sync already stored, not a second one");

        var afterReplay = await _flow.Public.JourneyAsync(journey.Id);
        Verify.Ok(afterReplay, "The public journey detail should still be readable after the replay");
        Verify.Unchanged(
            beforeReplay.Value!.RawJson,
            afterReplay.Value!.RawJson,
            "Replaying two queued logs should not create a second departure, event or progress update");

        var quiet = await stream.ReadAsync(TimeSpan.FromSeconds(2), stopAtFirstEvent: false);

        Verify.Equal(
            0,
            quiet.Events.Count,
            "The public stream should not deliver a replayed departure a second time");
    }

    [FlowFact]
    public async Task A_device_time_that_contradicts_a_later_stop_that_already_synced_is_refused()
    {
        // Arrange — the queue reached the server in reverse stop order, which the device times allow.
        var journey = await _flow.Journeys.CreateAsync(stopCount: 4);
        var laterStopTap = _flow.Now.AddMinutes(-20);
        var earlierStopTap = _flow.Now.AddMinutes(-19);

        Verify.Created(
            await _flow.Guard.LogDepartureAsync(journey.Id, 2, laterStopTap),
            "The Guard's queued log at stop 2, which the queue delivered before stop 1's,");

        // Act
        var contradiction = await _flow.Guard.LogDepartureAsync(journey.Id, 1, earlierStopTap);

        // Assert — the refusal leaves the journey's progress exactly as it was.
        Verify.BadRequest(
            contradiction,
            "A queued log for stop 1 whose device time is after stop 2's already synced departure should be refused");

        var detail = await _flow.Public.JourneyViewAsync(journey.Id);
        Verify.Ok(detail, "The public journey detail should be readable after the refused log");
        Verify.Equal<DateTimeOffset?>(
            null,
            detail.Value!.Stop(1).ActualDeparture,
            "The refused contradictory log should leave stop 1 without a recorded departure");
        Verify.Equal(
            laterStopTap,
            detail.Value.Stop(2).ActualDeparture,
            "The refused contradictory log should leave stop 2's progress as it was");
    }

    [FlowFact]
    public async Task A_log_for_another_guards_journey_is_refused_and_changes_nothing()
    {
        // Arrange
        var journey = await _flow.Journeys.CreateAsync(guardStaffId: StaffDirectory.SecondGuard.StaffId);
        var before = await _flow.Public.JourneyAsync(journey.Id);
        Verify.Ok(before, "The public journey detail should be readable before the refused log");

        // Act
        var refused = await _flow.Guard.LogDepartureAsync(
            journey.Id,
            stopSequence: 1,
            _flow.Now.AddMinutes(-20));

        // Assert
        Verify.Refused(
            refused,
            "A Guard's log for a journey assigned to another guard should be refused");

        var after = await _flow.Public.JourneyAsync(journey.Id);
        Verify.Ok(after, "The public journey detail should still be readable after the refusal");
        Verify.Equal<DateTimeOffset?>(
            null,
            (await _flow.Public.JourneyViewAsync(journey.Id)).Value!.Stop(1).ActualDeparture,
            "A Guard's refused log for another guard's journey should record no departure");
        Verify.Unchanged(
            before.Value!.RawJson,
            after.Value!.RawJson,
            "A Guard's refused log for another guard's journey should change nothing about the journey");
    }
}
