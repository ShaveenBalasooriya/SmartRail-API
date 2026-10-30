using System.Text.Json;
using SmartRail.Host.IntegrationTests.Flows.Support;
using SmartRail.Host.IntegrationTests.Infrastructure;
using SmartRail.SharedKernel.Application.Abstractions;

namespace SmartRail.Host.IntegrationTests.Flows;

/// <summary>
/// Flow 5, a wrong cancellation corrected, at flow level: the controller cancels a journey that never
/// left, every public channel says cancelled, then the correction withdraws the false state and every
/// channel says the journey runs as scheduled again, with the correction's own time as the note that
/// says it was corrected. The controller's view and the Guard's view agree about the corrected
/// journey; the public stream delivers the cancellation and then the correction in stream order; the
/// station display lists the journey again; the audit record keeps both entries, links the correction
/// to the entry it withdraws, and still verifies. The negatives are the route's own: an unknown
/// change, the same change corrected twice, a Guard's or an Auditor's correction, and a cancellation
/// of a journey that has already left or is completed are each refused and change nothing. The provider
/// is the fifth channel: the flow checks the deliveries a stub provider receiver accepts, their order
/// and the correction's link to the cancellation event.
/// </summary>
/// <remarks>
/// Every test builds its own journey through the real generation announcement and drives it with the
/// real controller routes on the Host's frozen clock, so the correction's published time, the audit
/// entry's time and the streams' order are exact. The shared audit record is append-only: the flow
/// reads only the entries its own journey produced and removes them again when it finishes.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class WrongCancellationCorrectedFlowTests : IAsyncLifetime
{
    private static readonly Dictionary<string, string> ProviderSettings = new()
    {
        ["Catering:ProviderApiKey"] = Drivers.ProviderDriver.ApiKey,
        ["Catering:Webhook:PollIntervalSeconds"] = "1",
        ["Catering:Webhook:RetryDelaysSeconds"] = "2,2,2,2,2",
        ["Catering:Webhook:RequestTimeoutSeconds"] = "5",
    };

    private readonly PostgresFixture _postgres;
    private FlowContext _flow = null!;

    public WrongCancellationCorrectedFlowTests(PostgresFixture postgres) => _postgres = postgres;

    /// <inheritdoc />
    public Task InitializeAsync()
    {
        _flow = new FlowContext(_postgres, settings: ProviderSettings);
        return _flow.InitializeAsync();
    }

    /// <inheritdoc />
    public Task DisposeAsync() => _flow.DisposeAsync();

    [Fact]
    public async Task A_cancelled_journey_is_shown_cancelled_and_the_correction_restores_it_with_its_time()
    {
        var journey = await _flow.Journeys.CreateAsync();

        var cancellation = await _flow.Changes.CancelAsync(journey);

        var cancelled = await _flow.Public.JourneyViewAsync(journey.Id);
        Verify.Ok(cancelled, "The public journey detail should be readable after the cancellation");
        Verify.Equal(
            "cancelled",
            cancelled.Value!.Status,
            "The public journey detail should show the journey the controller cancelled as cancelled");

        var correction = await _flow.Changes.CorrectAsync(cancellation.Id);

        var restored = await _flow.Public.JourneyViewAsync(journey.Id);
        Verify.Ok(restored, "The public journey detail should be readable after the correction");
        Verify.Equal(
            "scheduled",
            restored.Value!.Status,
            "The public journey detail should show the journey restored by the correction as scheduled");
        Verify.Equal(
            correction.PublishedAt,
            restored.Value.CorrectedAt,
            "The public journey detail should carry the correction's time as the note that it was corrected");
        Verify.Equal(
            null,
            restored.Value.Provenance,
            "The public journey detail should carry no disruption label once the cancellation is corrected");
    }

    [Fact]
    public async Task The_controller_and_guard_views_agree_about_the_corrected_journey()
    {
        var journey = await _flow.Journeys.CreateAsync();
        var cancellation = await _flow.Changes.CancelAsync(journey);

        var controllerCancelled = await _flow.ControllerWrites(StaffDirectory.Controller).JourneyViewAsync(journey.Id);
        Verify.Ok(controllerCancelled, "The controller's view should be readable after the cancellation");

        var guardCancelled = await _flow.Guard.JourneyViewAsync(journey.Id);
        Verify.Ok(guardCancelled, "The Guard's view should be readable after the cancellation");

        Verify.Equal(
            "cancelled",
            controllerCancelled.Value!.Status,
            "The controller's view should show the journey the controller cancelled as cancelled");
        Verify.Equal(
            controllerCancelled.Value.Render(),
            guardCancelled.Value!.Render(),
            "The controller's view and the Guard's view of the cancelled journey should agree");

        var correction = await _flow.Changes.CorrectAsync(cancellation.Id);

        var controller = await _flow.ControllerWrites(StaffDirectory.Controller).JourneyViewAsync(journey.Id);
        Verify.Ok(controller, "The controller's view should be readable after the correction");

        var guard = await _flow.Guard.JourneyViewAsync(journey.Id);
        Verify.Ok(guard, "The Guard's view should be readable for the journey assigned to the guard");

        Verify.Equal(
            controller.Value!.Render(),
            guard.Value!.Render(),
            "The controller's view and the Guard's view of the corrected journey should agree");
        Verify.Equal(
            "scheduled",
            guard.Value.Status,
            "The Guard's view should show the journey restored by the correction as scheduled");
        Verify.True(
            controller.Value.Change(cancellation.Id).Corrected,
            "The controller's view should mark the cancellation as corrected");
        Verify.Equal(
            correction.Id,
            guard.Value.Changes[1].Id,
            "The Guard's view should hold the correction as the second change on the journey");
        Verify.Equal(
            2,
            guard.Value.Changes.Count,
            "The Guard's view should keep both the cancellation and the correction");
    }

    [Fact]
    public async Task The_public_stream_delivers_the_cancellation_then_the_correction_in_stream_order()
    {
        var journey = await _flow.Journeys.CreateAsync();

        using var stream = await _flow.Public.OpenStreamAsync(journey.Id);

        var cancellation = await _flow.Changes.CancelAsync(journey);
        await _flow.Changes.CorrectAsync(cancellation.Id);

        var delivered = await stream.ReadAsync(
            Eventually.Timeout,
            stopAtFirstEvent: false,
            stopAfterEvents: 2);

        Verify.Equal(
            2,
            delivered.Events.Count,
            "The public stream should deliver the cancellation and the correction as two events");
        Verify.Equal(
            "cancellation_published",
            delivered.Events[0].EventType,
            "The public stream should deliver the cancellation first");
        Verify.Equal(
            "correction_published",
            delivered.Events[1].EventType,
            "The public stream should deliver the correction after the cancellation");
        Verify.True(
            delivered.Events[1].Position > delivered.Events[0].Position,
            "The public stream should deliver the correction at a later stream position than the cancellation");

        using var payload = JsonDocument.Parse(delivered.Events[1].Data);
        Verify.Equal(
            journey.Id,
            payload.RootElement.GetProperty("journeyId").GetGuid(),
            "The correction's stream event should carry the journey it corrected");
    }

    [Fact]
    public async Task The_audit_keeps_both_entries_and_the_correction_links_to_the_original()
    {
        var journey = await _flow.Journeys.CreateAsync();
        var cancellation = await _flow.Changes.CancelAsync(journey);
        await _flow.Changes.CorrectAsync(cancellation.Id);

        var entries = await _flow.Audit.JourneyEntriesAsync(journey.Id);

        Verify.Equal(
            2,
            entries.Count,
            "The audit record should keep one entry for the cancellation and one for the correction");
        Verify.Equal(
            "PublishCancellation, PublishCorrection",
            string.Join(", ", entries.Select(entry => entry.Action)),
            "The audit record should hold the cancellation before the correction");
        Verify.Equal(
            "succeeded, succeeded",
            string.Join(", ", entries.Select(entry => entry.Outcome)),
            "Both the cancellation and the correction should be recorded as succeeded actions");
        Verify.Equal(
            entries[0].Id,
            entries[1].RelatedEntryId,
            "The correction's audit entry should link to the entry of the change it withdrew");
        Verify.True(
            entries[1].ChainPosition > entries[0].ChainPosition,
            "The correction's audit entry should be appended after the cancellation's");

        var report = await _flow.Auditor.IntegrityAsync();
        Verify.Ok(report, "The Auditor should be able to check the audit chain after the correction");
        Verify.Equal(
            "intact",
            report.Value!.Status,
            "The audit chain should still verify after a correction that keeps both entries");
    }

    [Fact]
    public async Task The_display_feed_lists_the_journey_again_once_the_cancellation_is_corrected()
    {
        var journey = await _flow.Journeys.CreateAsync();

        var cancellation = await _flow.Changes.CancelAsync(journey);

        var cancelled = await _flow.Public.DisplayAsync(SeededNetwork.ColomboFort, "2");
        Verify.Ok(cancelled, "The station display feed should be readable after the cancellation");

        var cancelledItem = cancelled.Value!.ForJourney(journey.Id);
        Verify.True(
            cancelledItem is not null,
            "The station display feed should still list the cancelled journey within its grace window");
        Verify.Equal(
            "cancelled",
            cancelledItem!.Journey.Status,
            "The station display feed should show the cancelled journey as cancelled");

        var correction = await _flow.Changes.CorrectAsync(cancellation.Id);

        var restored = await _flow.Public.DisplayAsync(SeededNetwork.ColomboFort, "2");
        Verify.Ok(restored, "The station display feed should be readable after the correction");

        var restoredItem = restored.Value!.ForJourney(journey.Id);
        Verify.True(
            restoredItem is not null,
            "The station display feed should list the journey again after the correction");
        Verify.Equal(
            "scheduled",
            restoredItem!.Journey.Status,
            "The station display feed should show the journey restored by the correction as scheduled");
        Verify.Equal(
            correction.PublishedAt,
            restoredItem.Journey.CorrectedAt,
            "The station display feed should carry the correction's time as the note that it was corrected");
    }

    [Fact]
    public async Task Correcting_a_change_that_does_not_exist_is_refused()
    {
        var attempt = await _flow.ControllerWrites(StaffDirectory.Controller)
            .PublishCorrectionAsync(Guid.CreateVersion7());

        Verify.NotFound(attempt, "A correction of a change the record does not hold should be refused");
    }

    [Fact]
    public async Task Correcting_the_same_change_twice_is_refused_and_keeps_one_correction()
    {
        var journey = await _flow.Journeys.CreateAsync();
        var cancellation = await _flow.Changes.CancelAsync(journey);
        await _flow.Changes.CorrectAsync(cancellation.Id);

        var second = await _flow.ControllerWrites(StaffDirectory.Controller)
            .PublishCorrectionAsync(cancellation.Id);

        Verify.Conflict(second, "A second correction of the same change should be refused");

        var guard = await _flow.Guard.JourneyViewAsync(journey.Id);
        Verify.Ok(guard, "The Guard's view should be readable after the refused second correction");
        Verify.Equal(
            2,
            guard.Value!.Changes.Count,
            "The refused second correction should leave exactly the cancellation and the one correction");
    }

    [Theory]
    [InlineData(Roles.Guard)]
    [InlineData(Roles.Auditor)]
    public async Task A_guard_or_an_auditor_cannot_correct_and_the_journey_is_unchanged(string role)
    {
        var session = role == Roles.Guard ? StaffDirectory.Guard : StaffDirectory.Auditor;
        var journey = await _flow.Journeys.CreateAsync();
        var cancellation = await _flow.Changes.CancelAsync(journey);
        var before = await _flow.Internals.SnapshotAsync(journey.Id);
        var beforeDetail = await _flow.Public.JourneyViewAsync(journey.Id);
        var baseline = await _flow.Identity.MaxPositionAsync();

        var refused = await _flow.ControllerWrites(session).PublishCorrectionAsync(cancellation.Id);

        Verify.Refused(refused, $"A {role}'s correction should be refused");

        await _flow.Identity.WaitUntilDeliveredAsync(
            baseline,
            $"a {role}'s refused correction should be announced and delivered to the audit consumer");

        var after = await _flow.Internals.SnapshotAsync(journey.Id);
        Verify.Unchanged(
            before.Render(),
            after.Render(),
            $"A {role}'s refused correction should leave the journey's changes, events and outbox rows untouched");

        var afterDetail = await _flow.Public.JourneyViewAsync(journey.Id);
        Verify.Ok(afterDetail, $"The public journey detail should still be readable after a {role}'s refusal");
        Verify.Equal(
            beforeDetail.Value!.Status,
            afterDetail.Value!.Status,
            $"A {role}'s refused correction should leave the public status alone");
    }

    [Theory]
    [InlineData("departed")]
    [InlineData("completed")]
    public async Task A_cancellation_of_a_journey_that_is_no_longer_cancellable_is_refused(string state)
    {
        var journey = await _flow.Journeys.CreateAsync();

        if (state == "departed")
        {
            await _flow.Changes.MakeRunningAsync(journey);
        }
        else
        {
            await _flow.Internals.MarkCompletedAsync(journey.Id);
        }

        var before = await _flow.Internals.SnapshotAsync(journey.Id);

        var attempt = await _flow.ControllerWrites(StaffDirectory.Controller)
            .PublishCancellationAsync(journey.Id);

        Verify.Conflict(attempt, $"A cancellation of a journey that has {state} should be refused");

        var after = await _flow.Internals.SnapshotAsync(journey.Id);
        Verify.Unchanged(
            before.Render(),
            after.Render(),
            $"A refused cancellation of a journey that has {state} should publish no change, event or outbox row");
    }

    [Fact]
    public async Task The_provider_receives_the_cancellation_then_the_correction_that_references_it()
    {
        var journey = await _flow.Journeys.CreateAsync();
        await using var provider = await ProviderReceiver.StartAsync();

        await _flow.Provider.RegisterWebhookAsync(provider.CallbackUrl);

        var cancellation = await _flow.Changes.CancelAsync(journey);
        await _flow.Changes.CorrectAsync(cancellation.Id);

        await provider.WaitForDeliveriesAsync(
            2,
            "the provider should receive the cancellation and then the correction");

        var deliveries = provider.Deliveries.ToArray();

        Verify.Equal(
            "cancellation_published, correction_published",
            string.Join(", ", deliveries.Select(delivery => delivery.Type)),
            "The provider should receive the cancellation before the correction");
        Verify.True(
            deliveries[1].SequenceNumber > deliveries[0].SequenceNumber,
            "The correction's delivery should carry a later sequence number than the cancellation's");
        Verify.Equal(
            deliveries[0].EventId,
            deliveries[1].CorrectsEventId,
            "The correction's delivery should point at the cancellation event it withdraws");

        await _flow.Provider.WaitForDeliveredAsync(
            journey.Id,
            expected: 2,
            "both the cancellation's and the correction's deliveries should be marked delivered");
    }

    [Fact]
    public async Task The_provider_receives_one_journeys_changes_in_sequence_number_order()
    {
        var journey = await _flow.Journeys.CreateAsync();
        await using var provider = await ProviderReceiver.StartAsync();

        await _flow.Provider.RegisterWebhookAsync(provider.CallbackUrl);

        await _flow.Changes.DelayAsync(journey, fromStopSequence: 2, minutes: 5);
        var cancellation = await _flow.Changes.CancelAsync(journey);
        await _flow.Changes.CorrectAsync(cancellation.Id);

        await provider.WaitForDeliveriesAsync(
            3,
            "the provider should receive every change published on the journey");

        var deliveries = provider.Deliveries.ToArray();

        Verify.Equal(
            "1, 2, 3",
            string.Join(", ", deliveries.Select(delivery => delivery.SequenceNumber)),
            "One journey's deliveries should carry consecutive sequence numbers in publish order");
        Verify.Equal(
            "delay_published, cancellation_published, correction_published",
            string.Join(", ", deliveries.Select(delivery => delivery.Type)),
            "One journey's deliveries should arrive in the order its changes were published");
        Verify.True(
            deliveries.All(delivery => delivery.JourneyId == journey.Id),
            "Every one of the journey's deliveries should carry that journey");

        await _flow.Provider.WaitForDeliveredAsync(
            journey.Id,
            expected: 3,
            "every one of the journey's deliveries should be marked delivered");
    }
}
