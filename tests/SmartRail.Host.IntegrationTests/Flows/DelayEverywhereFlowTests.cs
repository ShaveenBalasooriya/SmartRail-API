using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartRail.Audit.Contracts;
using SmartRail.Host.IntegrationTests.Flows.Support;
using SmartRail.Host.IntegrationTests.Infrastructure;
using SmartRail.SharedKernel.Application.Abstractions;

namespace SmartRail.Host.IntegrationTests.Flows;

/// <summary>
/// Flow 1, a delay reaching every channel, at flow level: a controller publishes a fifteen-minute
/// delay from stop 3 of a running journey, and the stop it starts at and every later stop move fifteen
/// minutes later while the stops before it keep the times the departures give — on the passenger
/// detail, on the station display and in the Guard's view, each checked on its own channel. The public
/// stream delivers a delay_published event whose data shows the shifted times, and the Guard's stream
/// delivers the same event to the assigned guard and to no other. The audit record gains exactly one
/// entry for the change; the write is atomic, so a failing audit step leaves no change, event, outbox
/// row or shifted time; a reconnecting passenger is caught up on the missed delay exactly once and a
/// position that cannot be resumed from starts with a reset. The negatives are the route's own: a delay
/// outside the allowed range, a delay from a stop the journey does not call at and a delay on a
/// cancelled journey are refused. The provider is the fifth channel: the flow checks the deliveries a
/// stub provider receiver accepts, including a refusal that is retried, so the channel is proven without
/// the Docker simulated provider.
/// </summary>
/// <remarks>
/// The journeys are four calling stops so that a delay published at stop 3 has a later stop to reach,
/// and they are made running by a real Guard departure at stop 1, as a controller's delay on a
/// running journey is a change to a train already on its way. The Host's clock is frozen, so every
/// expected time is exact, and the flow removes the journeys, their rows and their audit entries.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class DelayEverywhereFlowTests : IAsyncLifetime
{
    private const int DelayMinutes = 15;
    private const int FromStop = 3;

    /// <summary>
    /// The provider channel's settings: the seeded dev key the provider calls with, and a dispatcher
    /// schedule short enough to observe a retry inside a test rather than the spec's minutes.
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

    [Fact]
    public async Task The_public_journey_detail_moves_the_delay_stop_and_later_stops_only()
    {
        var journey = await RunningFourStopJourneyAsync();
        var before = await _flow.Public.JourneyViewAsync(journey.Id);
        Verify.Ok(before, "The public journey detail should be readable before the delay");

        await _flow.Changes.DelayAsync(journey, FromStop, DelayMinutes);

        var after = await _flow.Public.JourneyViewAsync(journey.Id);
        Verify.Ok(after, "The public journey detail should be readable after the delay");

        Verify.Equal(
            before.Value!.Stop(1).ExpectedDeparture,
            after.Value!.Stop(1).ExpectedDeparture,
            "The public journey detail should leave the stop before the delay where it was");
        Verify.Equal(
            before.Value.Stop(2).ExpectedDeparture,
            after.Value.Stop(2).ExpectedDeparture,
            "The public journey detail should leave the second stop before the delay where it was");
        Verify.Equal(
            before.Value.Stop(3).ExpectedArrival!.Value.AddMinutes(DelayMinutes),
            after.Value.Stop(3).ExpectedArrival,
            "The public journey detail should move the stop the delay starts at by the published minutes");
        Verify.Equal(
            before.Value.Stop(3).ExpectedDeparture!.Value.AddMinutes(DelayMinutes),
            after.Value.Stop(3).ExpectedDeparture,
            "The public journey detail should move the delayed stop's departure by the published minutes");
        Verify.Equal(
            before.Value.Stop(4).ExpectedArrival!.Value.AddMinutes(DelayMinutes),
            after.Value.Stop(4).ExpectedArrival,
            "The public journey detail should carry the delay on to the last stop");
    }

    [Fact]
    public async Task The_station_display_moves_the_delay_stop_and_later_stops_only()
    {
        var journey = await RunningFourStopJourneyAsync();
        var before = await _flow.Public.DisplayAsync(SeededNetwork.Kollupitiya, "1");
        Verify.Ok(before, "The station display feed should be readable before the delay");

        var beforeItem = before.Value!.ForJourney(journey.Id);
        Verify.True(beforeItem is not null, "The station display feed should list the running journey before the delay");

        await _flow.Changes.DelayAsync(journey, FromStop, DelayMinutes);

        var after = await _flow.Public.DisplayAsync(SeededNetwork.Kollupitiya, "1");
        Verify.Ok(after, "The station display feed should be readable after the delay");

        var afterItem = after.Value!.ForJourney(journey.Id);
        Verify.True(afterItem is not null, "The station display feed should still list the running journey after the delay");

        Verify.Equal(
            beforeItem!.Journey.Stop(2).ExpectedDeparture,
            afterItem!.Journey.Stop(2).ExpectedDeparture,
            "The station display should leave the departure it shows where it was");
        Verify.Equal(
            beforeItem.Journey.Stop(3).ExpectedArrival!.Value.AddMinutes(DelayMinutes),
            afterItem.Journey.Stop(3).ExpectedArrival,
            "The station display should move the stop the delay starts at by the published minutes");
        Verify.Equal(
            beforeItem.Journey.Stop(4).ExpectedArrival!.Value.AddMinutes(DelayMinutes),
            afterItem.Journey.Stop(4).ExpectedArrival,
            "The station display should carry the delay on to the last stop");
    }

    [Fact]
    public async Task The_guard_view_moves_the_delay_stop_and_later_stops_only()
    {
        var journey = await RunningFourStopJourneyAsync();
        var before = await _flow.Guard.JourneyViewAsync(journey.Id);
        Verify.Ok(before, "The Guard's view should be readable before the delay");

        await _flow.Changes.DelayAsync(journey, FromStop, DelayMinutes);

        var after = await _flow.Guard.JourneyViewAsync(journey.Id);
        Verify.Ok(after, "The Guard's view should be readable after the delay");

        Verify.Equal(
            before.Value!.Stop(1).ExpectedDeparture,
            after.Value!.Stop(1).ExpectedDeparture,
            "The Guard's view should leave the stop before the delay where it was");
        Verify.Equal(
            before.Value.Stop(2).ExpectedArrival,
            after.Value.Stop(2).ExpectedArrival,
            "The Guard's view should leave the second stop before the delay where it was");
        Verify.Equal(
            before.Value.Stop(3).ExpectedArrival!.Value.AddMinutes(DelayMinutes),
            after.Value.Stop(3).ExpectedArrival,
            "The Guard's view should move the stop the delay starts at by the published minutes");
        Verify.Equal(
            before.Value.Stop(4).ExpectedArrival!.Value.AddMinutes(DelayMinutes),
            after.Value.Stop(4).ExpectedArrival,
            "The Guard's view should carry the delay on to the last stop");
    }

    [Fact]
    public async Task The_public_stream_delivers_a_delay_published_event_with_the_shifted_times()
    {
        var journey = await RunningFourStopJourneyAsync();
        var before = await _flow.Public.JourneyViewAsync(journey.Id);
        Verify.Ok(before, "The public journey detail should be readable before the delay");

        using var stream = await _flow.Public.OpenStreamAsync(journey.Id);

        await _flow.Changes.DelayAsync(journey, FromStop, DelayMinutes);

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
        var delayed = stops.Single(stop => stop.GetProperty("sequence").GetInt32() == FromStop);

        Verify.Equal(
            before.Value!.Stop(FromStop).ExpectedArrival!.Value.AddMinutes(DelayMinutes),
            delayed.GetProperty("expectedArrival").GetDateTimeOffset(),
            "The delay_published event's data should show the stop the delay starts at shifted by fifteen minutes");
        Verify.Equal(
            before.Value.Stop(FromStop + 1).ExpectedArrival!.Value.AddMinutes(DelayMinutes),
            stops.Single(stop => stop.GetProperty("sequence").GetInt32() == FromStop + 1)
                .GetProperty("expectedArrival").GetDateTimeOffset(),
            "The delay_published event's data should show the last stop shifted by fifteen minutes");
    }

    [Fact]
    public async Task The_guard_stream_delivers_the_delay_to_the_assigned_guard_and_not_to_another()
    {
        var assigned = await RunningFourStopJourneyAsync();
        var unassigned = await _flow.Journeys.CreateAsync(
            guardStaffId: StaffDirectory.SecondGuard.StaffId,
            stopCount: 4);

        var guard = new Drivers.GuardDriver(_flow.Api, StaffDirectory.Guard);
        using var stream = await guard.OpenStreamAsync();

        await _flow.Changes.DelayAsync(assigned, FromStop, DelayMinutes);

        var delivered = await stream.ReadAsync(Eventually.Timeout, stopAtFirstEvent: true);

        Verify.Equal(
            1,
            delivered.Events.Count,
            "The Guard's stream should deliver the delay published on a journey assigned to that guard");
        Verify.Equal(
            "delay_published",
            delivered.Events[0].EventType,
            "The Guard's stream should deliver the published delay as a delay_published event");

        using (var payload = JsonDocument.Parse(delivered.Events[0].Data))
        {
            Verify.Equal(
                assigned.Id,
                payload.RootElement.GetProperty("journeyId").GetGuid(),
                "The Guard's stream event should carry the assigned journey");
        }

        // The other journey is real and committed, but it is not this guard's: the connection is
        // scoped by the credential alone, so it never hears about it.
        await _flow.Changes.DelayAsync(unassigned, FromStop, DelayMinutes);

        var quiet = await stream.ReadAsync(TimeSpan.FromSeconds(2), stopAtFirstEvent: false);

        Verify.Equal(
            0,
            quiet.Events.Count,
            "The Guard's stream should not deliver a change on a journey assigned to another guard");
    }

    [Fact]
    public async Task Publishing_a_delay_appends_exactly_one_audit_entry_for_the_change()
    {
        var journey = await RunningFourStopJourneyAsync();
        var baseline = await _flow.Audit.MaxChainPositionAsync();

        await _flow.Changes.DelayAsync(journey, FromStop, DelayMinutes);

        var entries = await _flow.Audit.SucceededEntriesAfterAsync(baseline);

        Verify.Equal(
            1,
            entries.Count,
            "Publishing one delay should append exactly one successful audit entry from the flow's baseline");
        Verify.Equal(
            "PublishDelay",
            entries[0].Action,
            "The delay's audit entry should name the publish action");
        Verify.Equal(
            Roles.NetworkController,
            entries[0].ActorRole,
            "The delay's audit entry should name the controller's role");
        Verify.Equal(
            _flow.Now,
            entries[0].OccurredAt,
            "The delay's audit entry should carry the server time of the publish");
    }

    [Fact]
    public async Task A_failing_audit_step_leaves_no_delay_event_outbox_row_or_shifted_time()
    {
        var failing = new FlowContext(
            _postgres,
            services =>
            {
                services.RemoveAll<IAuditRecorder>();
                services.AddScoped<IAuditRecorder, ThrowingAuditRecorder>();
            });

        await failing.InitializeAsync();

        try
        {
            var journey = await failing.Journeys.CreateAsync();
            var before = await failing.Internals.SnapshotAsync(journey.Id);
            var beforeDetail = await failing.Public.JourneyViewAsync(journey.Id);
            Verify.Ok(beforeDetail, "The public journey detail should be readable before the failing publish");

            var attempt = await failing.ControllerWrites(StaffDirectory.Controller)
                .PublishDelayAsync(journey.Id, FromStop, DelayMinutes);

            Verify.Equal(
                HttpStatusCode.InternalServerError,
                attempt.Status,
                "A delay whose audit step fails should answer 500 rather than a successful publish");

            var after = await failing.Internals.SnapshotAsync(journey.Id);
            Verify.Unchanged(
                before.Render(),
                after.Render(),
                "A delay whose audit step fails should leave no change, no journey event and no outbox row");

            var afterDetail = await failing.Public.JourneyViewAsync(journey.Id);
            Verify.Ok(afterDetail, "The public journey detail should still be readable after the failing publish");
            Verify.Equal(
                beforeDetail.Value!.Stop(FromStop).ExpectedArrival,
                afterDetail.Value!.Stop(FromStop).ExpectedArrival,
                "A delay whose audit step fails should leave the stop's expected arrival unshifted");
        }
        finally
        {
            await failing.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_reconnecting_public_stream_delivers_the_missed_delay_exactly_once()
    {
        var journey = await RunningFourStopJourneyAsync();

        // The delay the passenger app had already received before it lost the connection.
        await _flow.Changes.DelayAsync(journey, FromStop, 5);
        var lastSeen = (await _flow.Internals.LatestStreamPositionAsync(journey.Id))
            .ToString(System.Globalization.CultureInfo.InvariantCulture);

        // Published while the passenger app was disconnected: only the catch-up can deliver it. The
        // announcement is dispatched first, so what arrives after the join cannot be this change's own
        // delivery arriving late.
        await _flow.Changes.DelayAsync(journey, FromStop, 10);
        await _flow.Internals.WaitForAnnouncementsDeliveredAsync(
            journey.Id,
            "the missed delay's announcement should be dispatched before the stream resumes");

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

        var quiet = await resumed.ReadAsync(TimeSpan.FromSeconds(2), stopAtFirstEvent: false);
        Verify.Equal(
            0,
            quiet.Events.Count,
            "The resumed stream should deliver the missed delay exactly once and then stay quiet, but received "
            + $"[{string.Join(", ", quiet.Events.Select(frame => $"{frame.EventType}#{frame.Id}"))}] "
            + $"after caught-up {caughtUp.Events[0].EventType}#{caughtUp.Events[0].Id} and lastSeen={lastSeen}");
    }

    [Fact]
    public async Task A_stream_whose_position_is_unknown_starts_with_a_reset()
    {
        var journey = await RunningFourStopJourneyAsync();

        using var resumed = await _flow.Public.ResumeStreamAsync(journey.Id, "not-a-position");

        var reset = await resumed.ReadAsync(Eventually.Timeout, stopAtFirstEvent: true);

        Verify.Equal(
            1,
            reset.Events.Count,
            "A stream resumed from an unusable position should first receive exactly one frame");
        Verify.Equal(
            "reset",
            reset.Events[0].EventType,
            "A stream resumed from an unusable position should be told to reset");
        Verify.Equal<object?>(
            null,
            reset.Events[0].Id,
            "The reset frame should carry no stream position");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(721)]
    public async Task A_delay_outside_the_allowed_range_is_refused(int minutes)
    {
        var journey = await _flow.Journeys.CreateAsync(stopCount: 4);

        var attempt = await _flow.ControllerWrites(StaffDirectory.Controller)
            .PublishDelayAsync(journey.Id, FromStop, minutes);

        Verify.BadRequest(attempt, $"A delay of {minutes} minutes should be refused as outside the allowed range");

        Verify.Empty(
            [.. (await _flow.Public.JourneyViewAsync(journey.Id)).Value!.Stops.Where(stop => stop.Provenance is not null)],
            "A refused out-of-range delay should leave no stop carrying a disruption label");
    }

    [Fact]
    public async Task A_delay_from_a_stop_the_journey_does_not_call_at_is_refused()
    {
        var journey = await _flow.Journeys.CreateAsync(stopCount: 4);
        var before = await _flow.Public.JourneyViewAsync(journey.Id);
        Verify.Ok(before, "The public journey detail should be readable before the refused delay");

        var attempt = await _flow.ControllerWrites(StaffDirectory.Controller)
            .PublishDelayAsync(journey.Id, fromStopSequence: 9, DelayMinutes);

        Verify.BadRequest(attempt, "A delay from a stop the journey does not call at should be refused");

        var after = await _flow.Public.JourneyViewAsync(journey.Id);
        Verify.Ok(after, "The public journey detail should still be readable after the refused delay");
        Verify.Equal(
            before.Value!.Stop(FromStop).ExpectedArrival,
            after.Value!.Stop(FromStop).ExpectedArrival,
            "A refused delay from an unknown stop should leave the journey's expected times alone");
    }

    [Fact]
    public async Task A_delay_on_a_cancelled_journey_is_refused()
    {
        var journey = await _flow.Journeys.CreateAsync(stopCount: 4);
        await _flow.Changes.CancelAsync(journey);
        var before = await _flow.Public.JourneyViewAsync(journey.Id);
        Verify.Ok(before, "The public journey detail should be readable after the cancellation");

        var attempt = await _flow.ControllerWrites(StaffDirectory.Controller)
            .PublishDelayAsync(journey.Id, FromStop, DelayMinutes);

        Verify.Conflict(attempt, "A delay on a cancelled journey should be refused");

        var after = await _flow.Public.JourneyViewAsync(journey.Id);
        Verify.Ok(after, "The public journey detail should still be readable after the refused delay");
        Verify.Equal(
            "cancelled",
            after.Value!.Status,
            "A refused delay should leave the cancelled journey cancelled");
        Verify.Equal(
            before.Value!.Stop(FromStop).ExpectedArrival,
            after.Value.Stop(FromStop).ExpectedArrival,
            "A refused delay on a cancelled journey should not shift any expected time");
    }

    /// <summary>
    /// The provider is the fifth channel of the flow: the subscription is real, the dispatcher is real
    /// and the delivery row is real; only the receiving server is a stub, which is what Layer A can
    /// hold without the Docker simulated provider.
    /// </summary>
    [Fact]
    public async Task The_provider_receives_the_delay_published_event_once_with_its_minutes()
    {
        var journey = await RunningFourStopJourneyAsync();
        await using var provider = await ProviderReceiver.StartAsync();

        await _flow.Provider.RegisterWebhookAsync(provider.CallbackUrl);

        // The delay starts where the journey calls next, so the event's own values carry the minutes
        // the provider has to apply.
        await _flow.Changes.DelayAsync(journey, fromStopSequence: 2, DelayMinutes);

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

        await _flow.Provider.WaitForDeliveredAsync(
            journey.Id,
            expected: 1,
            "the delay's delivery should be marked delivered once the provider accepted it");
    }

    [Fact]
    public async Task A_provider_that_is_down_is_retried_until_the_delay_is_applied_once()
    {
        var journey = await RunningFourStopJourneyAsync();
        await using var provider = await ProviderReceiver.StartAsync(failuresBeforeSuccess: 1);

        await _flow.Provider.RegisterWebhookAsync(provider.CallbackUrl);

        await _flow.Changes.DelayAsync(journey, fromStopSequence: 2, DelayMinutes);

        // The first attempt answers 503 and is recorded; the retry is scheduled on the Host's clock,
        // so the flow moves that clock past the retry delay instead of waiting the real time.
        await provider.WaitForDeliveriesAsync(
            1,
            "the provider should be sent the delay the first time");

        _flow.Clock.Advance(TimeSpan.FromSeconds(3));

        await provider.WaitForDeliveriesAsync(
            2,
            "the provider should be sent the delay again after answering 503");

        var deliveries = provider.Deliveries.ToArray();
        Verify.Equal(2, deliveries.Length, "The provider stub should have seen the first attempt and the retry");
        Verify.Equal(
            deliveries[0].EventId,
            deliveries[1].EventId,
            "The retry should carry the same event as the attempt the provider refused");
        Verify.Equal(
            2,
            await _flow.Provider.AttemptCountAsync(journey.Id, deliveries[0].SequenceNumber),
            "The delivery row should record the refused attempt and the accepted retry");

        await _flow.Provider.WaitForDeliveredAsync(
            journey.Id,
            expected: 1,
            "the delay should be marked delivered exactly once after the retry");
    }

    /// <summary>
    /// A four-stop journey whose first stop departed on time ten minutes before the frozen clock, as a
    /// controller's delay on a running journey needs: the journey is running and every expected time
    /// is still the timetabled one, so the delay's shift is exactly the published minutes.
    /// </summary>
    private async Task<Builders.Journey> RunningFourStopJourneyAsync()
    {
        var departure = _flow.Now.AddMinutes(-10);
        var journey = await _flow.Journeys.CreateAsync(stopCount: 4, firstDeparture: departure);

        await _flow.Changes.MakeRunningAsync(journey, departure);

        // The departure's own progress event must have reached the channels before a stream opens,
        // or a stream would receive it and the delay together.
        await _flow.Internals.WaitForAnnouncementsDeliveredAsync(
            journey.Id,
            "the journey's departure announcement should reach every channel before the delay is published");

        return journey;
    }
}
