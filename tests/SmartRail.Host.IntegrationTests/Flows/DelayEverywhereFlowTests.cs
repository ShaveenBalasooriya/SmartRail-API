using System.Text.Json;
using SmartRail.Host.IntegrationTests.Flows.Support;
using SmartRail.Host.IntegrationTests.Infrastructure;
using SmartRail.SharedKernel.Application.Abstractions;

namespace SmartRail.Host.IntegrationTests.Flows;

/// <summary>
/// Flow 1, a delay reaching every channel, at flow level: a controller publishes a fifteen-minute delay
/// from the next calling stop of a running four-stop journey, and the stop it starts at and every later
/// stop move fifteen minutes later while the stop before it keeps the times the departure gives — on the
/// passenger detail, on the station display and in the Guard's view. The public stream delivers one
/// <c>delay_published</c> frame whose data shows the shifted times, the provider receives one delivery
/// with its minutes and provenance, and the audit record gains one succeeded entry naming the
/// controller. The flow's other cases are the story's own edges: a client that missed a delay catches it
/// up exactly once on reconnect, a delay on a cancelled journey is a conflict, and a delay from a stop
/// the journey does not call at is a bad request.
/// </summary>
/// <remarks>
/// The journey is four calling stops and was made running by a real Guard departure at stop 1, as a
/// controller's delay on a running journey is a change to a train already on its way; the delay starts
/// where the train calls next, which is the stop the provider's minutes are measured at. The Host's clock
/// is frozen, so every expected time is exact. Every channel is read through its public interface — the
/// detail, the display, the Guard's view, the stream, the stub provider receiver and the Auditor's list —
/// and the flow removes the rows it added through <see cref="FlowCleanup"/> when it finishes.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class DelayEverywhereFlowTests : IAsyncLifetime
{
    private const int DelayMinutes = 15;
    private const int FromStop = 2;

    /// <summary>
    /// The provider channel's settings: the seeded dev key the provider calls with, and a dispatcher
    /// schedule short enough to observe a delivery inside a test rather than the spec's minutes.
    /// </summary>
    private static readonly Dictionary<string, string> ProviderSettings = new()
    {
        ["Catering:ProviderApiKey"] = Drivers.ProviderDriver.ApiKey,
        ["Catering:Webhook:PollIntervalSeconds"] = "1",
        ["Catering:Webhook:RetryDelaysSeconds"] = "2,2,2,2,2",
        ["Catering:Webhook:RequestTimeoutSeconds"] = "5",
    };

    private readonly PostgresFixture _postgres;
    private FlowContext _flow = null!;

    public DelayEverywhereFlowTests(PostgresFixture postgres) => _postgres = postgres;

    /// <inheritdoc />
    public Task InitializeAsync()
    {
        _flow = new FlowContext(_postgres, settings: ProviderSettings);
        return _flow.InitializeAsync();
    }

    /// <inheritdoc />
    public Task DisposeAsync() => _flow.DisposeAsync();

    [FlowFact]
    public async Task Happy_path_a_delay_reaches_every_channel_and_is_audited_once()
    {
        // Arrange
        var (journey, stream) = await RunningFourStopJourneyAsync();
        using var streamLifetime = stream;
        await using var provider = await ProviderReceiver.StartAsync();
        await _flow.Provider.RegisterWebhookAsync(provider.CallbackUrl);

        var before = await _flow.Public.JourneyViewAsync(journey.Id);
        Verify.Ok(before, "The public journey detail should be readable before the delay");

        var beforeBoard = await _flow.Public.DisplayAsync(SeededNetwork.Kollupitiya, "1");
        Verify.Ok(beforeBoard, "The station display feed should be readable before the delay");
        var beforeItem = beforeBoard.Value!.ForJourney(journey.Id);
        Verify.True(
            beforeItem is not null,
            "The station display feed should list the running journey before the delay");

        var beforeGuard = await _flow.Guard.JourneyViewAsync(journey.Id);
        Verify.Ok(beforeGuard, "The Guard's view should be readable before the delay");

        // Act
        await _flow.Changes.DelayAsync(journey, FromStop, DelayMinutes);

        // Assert — the passenger's detail moves the delayed stop and later stops only.
        var after = await _flow.Public.JourneyViewAsync(journey.Id);
        Verify.Ok(after, "The public journey detail should be readable after the delay");

        Verify.Equal(
            before.Value!.Stop(1).ExpectedDeparture,
            after.Value!.Stop(1).ExpectedDeparture,
            "The public journey detail should leave the stop before the delay where it was");
        Verify.Equal(
            before.Value.Stop(2).ExpectedArrival!.Value.AddMinutes(DelayMinutes),
            after.Value.Stop(2).ExpectedArrival,
            "The public journey detail should move the delayed stop's arrival by the published minutes");
        Verify.Equal(
            before.Value.Stop(2).ExpectedDeparture!.Value.AddMinutes(DelayMinutes),
            after.Value.Stop(2).ExpectedDeparture,
            "The public journey detail should move the stop the delay starts at by the published minutes");
        Verify.Equal(
            before.Value.Stop(3).ExpectedArrival!.Value.AddMinutes(DelayMinutes),
            after.Value.Stop(3).ExpectedArrival,
            "The public journey detail should carry the delay on to the next stop");
        Verify.Equal(
            before.Value.Stop(4).ExpectedArrival!.Value.AddMinutes(DelayMinutes),
            after.Value.Stop(4).ExpectedArrival,
            "The public journey detail should carry the delay on to the last stop");

        // Assert — the station display shows the same shift on the departure it shows.
        var afterBoard = await _flow.Public.DisplayAsync(SeededNetwork.Kollupitiya, "1");
        Verify.Ok(afterBoard, "The station display feed should be readable after the delay");
        var afterItem = afterBoard.Value!.ForJourney(journey.Id);
        Verify.True(
            afterItem is not null,
            "The station display feed should still list the running journey after the delay");

        Verify.Equal(
            beforeItem!.Journey.Stop(1).ExpectedDeparture,
            afterItem!.Journey.Stop(1).ExpectedDeparture,
            "The station display should leave the previous stop's departure where it was");
        Verify.Equal(
            beforeItem.Journey.Stop(2).ExpectedDeparture!.Value.AddMinutes(DelayMinutes),
            afterItem.Journey.Stop(2).ExpectedDeparture,
            "The station display should move the departure it shows by the published minutes");
        Verify.Equal(
            beforeItem.Journey.Stop(3).ExpectedArrival!.Value.AddMinutes(DelayMinutes),
            afterItem.Journey.Stop(3).ExpectedArrival,
            "The station display should carry the delay on to the next stop");
        Verify.Equal(
            beforeItem.Journey.Stop(4).ExpectedArrival!.Value.AddMinutes(DelayMinutes),
            afterItem.Journey.Stop(4).ExpectedArrival,
            "The station display should carry the delay on to the last stop");
        Verify.Equal(
            DelayMinutes,
            afterItem.MinutesLate,
            "The station display should show the departure as the published minutes late");

        // Assert — the Guard sees the delay on the journey they are working.
        var afterGuard = await _flow.Guard.JourneyViewAsync(journey.Id);
        Verify.Ok(afterGuard, "The Guard's view should be readable after the delay");

        Verify.Equal(
            beforeGuard.Value!.Stop(1).ExpectedDeparture,
            afterGuard.Value!.Stop(1).ExpectedDeparture,
            "The Guard's view should leave the stop before the delay where it was");
        Verify.Equal(
            beforeGuard.Value.Stop(2).ExpectedArrival!.Value.AddMinutes(DelayMinutes),
            afterGuard.Value.Stop(2).ExpectedArrival,
            "The Guard's view should move the stop the delay starts at by the published minutes");
        Verify.Equal(
            beforeGuard.Value.Stop(3).ExpectedArrival!.Value.AddMinutes(DelayMinutes),
            afterGuard.Value.Stop(3).ExpectedArrival,
            "The Guard's view should carry the delay on to the next stop");
        Verify.Equal(
            beforeGuard.Value.Stop(4).ExpectedArrival!.Value.AddMinutes(DelayMinutes),
            afterGuard.Value.Stop(4).ExpectedArrival,
            "The Guard's view should carry the delay on to the last stop");

        // Assert — the live channel delivers one frame for the delay, carrying the shifted times.
        var delivered = await stream.ReadAsync(Eventually.Timeout, stopAtFirstEvent: true);

        Verify.Equal(
            1,
            delivered.Events.Count,
            "The public stream should deliver the published delay as exactly one event");
        Verify.Equal(
            "delay_published",
            delivered.Events[0].EventType,
            "The public stream should deliver the published delay as a delay_published event");

        using var payload = JsonDocument.Parse(delivered.Events[0].Data);
        var stops = payload.RootElement.GetProperty("stops").EnumerateArray().ToArray();

        Verify.Equal(
            before.Value.Stop(FromStop).ExpectedArrival!.Value.AddMinutes(DelayMinutes),
            stops.Single(stop => stop.GetProperty("sequence").GetInt32() == FromStop)
                .GetProperty("expectedArrival").GetDateTimeOffset(),
            "The delay_published event's data should show the stop the delay starts at shifted by fifteen minutes");
        Verify.Equal(
            before.Value.Stop(FromStop + 1).ExpectedArrival!.Value.AddMinutes(DelayMinutes),
            stops.Single(stop => stop.GetProperty("sequence").GetInt32() == FromStop + 1)
                .GetProperty("expectedArrival").GetDateTimeOffset(),
            "The delay_published event's data should show the next stop shifted by fifteen minutes");

        // Assert — the provider is the fifth channel: one delivery with the minutes it has to apply.
        await provider.WaitForDeliveriesAsync(
            1,
            "the provider should receive the delay published on the journey it subscribes to");
        await provider.WaitQuietAsync(
            TimeSpan.FromSeconds(2),
            expectedCount: 1,
            "the provider should receive the published delay exactly once");

        var delivery = Assert.Single(provider.Deliveries);

        Verify.Equal("delay_published", delivery.Type, "The provider's delivery should be a delay_published event");
        Verify.Equal(journey.Id, delivery.JourneyId, "The provider's delivery should carry the journey that was delayed");
        Verify.Equal(
            DelayMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            delivery.Values["delayMinutes"],
            "The provider's delivery should carry the delay's fifteen minutes");
        Verify.Equal(
            "automatically_published",
            delivery.Provenance,
            "The provider's delivery should carry the automatically-published provenance");

        // Assert — the change is audited once, naming the controller and the server time.
        var entry = await _flow.Auditor.WaitForJourneyEntryAsync(
            journey.Id,
            "PublishDelay",
            "the delay the controller published should append one audit entry for the journey");

        Verify.Equal("succeeded", entry.Outcome, "The delay's audit entry should be recorded as succeeded");
        Verify.Equal(
            StaffDirectory.ControllerId,
            entry.ActorStaffId,
            "The delay's audit entry should name the controller who published it");
        Verify.Equal(
            Roles.NetworkController,
            entry.ActorRole,
            "The delay's audit entry should name the controller's role");
        Verify.Equal(
            _flow.Now,
            entry.OccurredAt,
            "The delay's audit entry should carry the server time of the publish");
    }

    [FlowFact]
    public async Task Happy_path_a_reconnecting_client_is_delivered_the_missed_delay_exactly_once()
    {
        // Arrange — a client follows the journey, receives one delay, and then loses its connection.
        var journey = await _flow.Journeys.CreateAsync(stopCount: 4);
        var lastSeen = await FirstDelayPositionAsync(journey);

        // Act — a second delay is published while the client is disconnected.
        await _flow.Changes.DelayAsync(journey, FromStop, 10);

        // Assert — the resumed stream delivers that one missed delay, after the position it last saw.
        using var resumed = await _flow.Public.ResumeStreamAsync(journey.Id, lastSeen);
        var caughtUp = await resumed.ReadAsync(Eventually.Timeout, stopAtFirstEvent: true);

        Verify.Equal(
            1,
            caughtUp.Events.Count,
            "The resumed stream should deliver the one delay published while it was disconnected");
        Verify.Equal(
            "delay_published",
            caughtUp.Events[0].EventType,
            "The resumed stream should deliver the missed delay as a delay_published event, but received "
            + $"[{string.Join(", ", caughtUp.Events.Select(frame => $"{frame.EventType}#{frame.Id}"))}] "
            + $"after lastSeen={lastSeen}");
        Verify.True(
            caughtUp.Events[0].Position > long.Parse(lastSeen, System.Globalization.CultureInfo.InvariantCulture),
            "The resumed stream should deliver the missed delay after the position the client last saw");

        // Assert — it arrives once: nothing follows it on the same connection.
        var quiet = await resumed.ReadAsync(TimeSpan.FromSeconds(2), stopAtFirstEvent: false);

        Verify.Equal(
            0,
            quiet.Events.Count,
            "The resumed stream should deliver the missed delay exactly once and then stay quiet, but received "
            + $"[{string.Join(", ", quiet.Events.Select(frame => $"{frame.EventType}#{frame.Id}"))}] "
            + $"after caught-up {caughtUp.Events[0].EventType}#{caughtUp.Events[0].Id} and lastSeen={lastSeen}");
    }

    [FlowFact]
    public async Task A_delay_on_a_cancelled_journey_is_a_conflict_and_leaves_the_detail_unchanged()
    {
        // Arrange
        var journey = await _flow.Journeys.CreateAsync(stopCount: 4);
        await _flow.Changes.CancelAsync(journey);

        var before = await _flow.Public.JourneyAsync(journey.Id);
        Verify.Ok(before, "The public journey detail should be readable after the cancellation");

        // Act
        var attempt = await _flow.ControllerWrites(StaffDirectory.Controller)
            .PublishDelayAsync(journey.Id, FromStop, DelayMinutes);

        // Assert
        Verify.Conflict(attempt, "A delay on a cancelled journey should be refused");

        var after = await _flow.Public.JourneyAsync(journey.Id);
        Verify.Ok(after, "The public journey detail should still be readable after the refused delay");
        Verify.Unchanged(
            before.Value!.RawJson,
            after.Value!.RawJson,
            "A refused delay on a cancelled journey should leave the public journey detail unchanged");
    }

    [FlowFact]
    public async Task A_delay_from_a_stop_the_journey_does_not_call_at_is_a_bad_request()
    {
        // Arrange
        var journey = await _flow.Journeys.CreateAsync(stopCount: 4);

        // Act
        var attempt = await _flow.ControllerWrites(StaffDirectory.Controller)
            .PublishDelayAsync(journey.Id, fromStopSequence: 9, DelayMinutes);

        // Assert
        Verify.BadRequest(attempt, "A delay from a stop the journey does not call at should be refused");
    }

    /// <summary>
    /// A four-stop journey whose first stop departed on time ten minutes before the frozen clock, as a
    /// controller's delay on a running journey needs: the journey is running and every expected time is
    /// still the timetabled one, so the delay's shift is exactly the published minutes. The public stream
    /// is opened first and its departure frame consumed, so the delay's frame is the next one it reads
    /// rather than one of two.
    /// </summary>
    private async Task<(Builders.Journey Journey, Drivers.SseStream Stream)> RunningFourStopJourneyAsync()
    {
        var departure = _flow.Now.AddMinutes(-10);
        var journey = await _flow.Journeys.CreateAsync(stopCount: 4, firstDeparture: departure);

        var stream = await _flow.Public.OpenStreamAsync(journey.Id);

        await _flow.Changes.MakeRunningAsync(journey, departure);

        var progress = await stream.ReadAsync(Eventually.Timeout, stopAtFirstEvent: true);

        Verify.Equal(
            1,
            progress.Events.Count,
            "The public stream should deliver the departure that makes the journey running");
        Verify.Equal(
            "departure_logged",
            progress.Events[0].EventType,
            "The public stream should deliver the running journey's departure as a departure_logged event");

        return (journey, stream);
    }

    /// <summary>
    /// Opens a stream on a fresh journey, publishes the first delay, and returns the position of the frame
    /// the client received: the position a reconnect resumes from, taken from the stream itself rather
    /// than from the stored event.
    /// </summary>
    private async Task<string> FirstDelayPositionAsync(Builders.Journey journey)
    {
        using var stream = await _flow.Public.OpenStreamAsync(journey.Id);

        await _flow.Changes.DelayAsync(journey, FromStop, 5);

        var received = await stream.ReadAsync(Eventually.Timeout, stopAtFirstEvent: true);

        Verify.Equal(
            1,
            received.Events.Count,
            "The connected client should receive the first delay as exactly one event");
        Verify.Equal(
            "delay_published",
            received.Events[0].EventType,
            "The connected client should receive the first delay as a delay_published event");
        Verify.True(
            received.Events[0].Id is not null,
            "The connected client's frame should carry the stream position a reconnect resumes from");

        return received.Events[0].Id!;
    }
}
