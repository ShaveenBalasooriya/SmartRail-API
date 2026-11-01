using SmartRail.Host.IntegrationTests.Flows.Support;
using SmartRail.Host.IntegrationTests.Infrastructure;

namespace SmartRail.Host.IntegrationTests.Flows;

/// <summary>
/// Flow 5, a wrong cancellation corrected, at flow level: the controller cancels a journey that never
/// left, every public channel says cancelled, then the correction withdraws the false state and the
/// channels say the journey runs as scheduled again, with the correction's own time as the note that says
/// it was corrected. The station display lists the journey again, the controller's view and the Guard's
/// view agree, the audit record keeps both entries with the correction linked to the one it withdraws and
/// still verifies, and the provider receives the cancellation and then the correction that references it.
/// The story's own edge is that correcting the same change twice is a conflict and leaves one correction.
/// </summary>
/// <remarks>
/// Every test builds its own journey through the real generation announcement and drives it with the real
/// controller routes on the Host's frozen clock, so the correction's published time and the audit
/// entries' times are exact. The shared audit record is append-only: the flow reads only the entries that
/// name its own journey, through the Auditor's API, and removes them again when it finishes.
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

    [FlowFact]
    public async Task Happy_path_a_corrected_cancellation_restores_the_detail_the_display_and_both_views()
    {
        // Arrange
        var journey = await _flow.Journeys.CreateAsync();
        var cancellation = await _flow.Changes.CancelAsync(journey);

        var cancelled = await _flow.Public.JourneyViewAsync(journey.Id);
        Verify.Ok(cancelled, "The public journey detail should be readable after the cancellation");
        Verify.Equal(
            "cancelled",
            cancelled.Value!.Status,
            "The public journey detail should show the journey the controller cancelled as cancelled");

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

        var cancelledBoard = await _flow.Public.DisplayAsync(SeededNetwork.ColomboFort, "2");
        Verify.Ok(cancelledBoard, "The station display feed should be readable after the cancellation");

        var cancelledItem = cancelledBoard.Value!.ForJourney(journey.Id);
        Verify.True(
            cancelledItem is not null,
            "The station display feed should still list the cancelled journey within its grace window");
        Verify.Equal(
            "cancelled",
            cancelledItem!.Journey.Status,
            "The station display feed should show the cancelled journey as cancelled");

        // Act
        var correction = await _flow.Changes.CorrectAsync(cancellation.Id);

        // Assert — the passenger's detail is the scheduled journey again, carrying the correction's time.
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

        // Assert — the display lists the journey again, on the same terms as the detail.
        var restoredBoard = await _flow.Public.DisplayAsync(SeededNetwork.ColomboFort, "2");
        Verify.Ok(restoredBoard, "The station display feed should be readable after the correction");

        var restoredItem = restoredBoard.Value!.ForJourney(journey.Id);
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

        // Assert — the controller and the Guard see the same corrected journey, with both changes kept.
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
            2,
            guard.Value.Changes.Count,
            "The Guard's view should keep both the cancellation and the correction");
        Verify.Equal(
            correction.Id,
            guard.Value.Changes[1].Id,
            "The Guard's view should hold the correction as the second change on the journey");
    }

    [FlowFact]
    public async Task Happy_path_the_audit_keeps_both_entries_and_links_the_correction_to_the_original()
    {
        // Arrange
        var journey = await _flow.Journeys.CreateAsync();
        var cancellation = await _flow.Changes.CancelAsync(journey);
        var correction = await _flow.Changes.CorrectAsync(cancellation.Id);

        // Act — the record of this journey, read through the Auditor's API.
        var entries = await _flow.Auditor.WaitForJourneyEntriesAsync(
            journey.Id,
            expected: 2,
            "the cancellation and the correction should both be recorded against the journey");

        // Assert
        Verify.Equal(
            "PublishCancellation, PublishCorrection",
            string.Join(", ", entries.Select(entry => entry.Action)),
            "The audit record should hold the cancellation before the correction");
        Verify.Equal(
            "succeeded, succeeded",
            string.Join(", ", entries.Select(entry => entry.Outcome)),
            "Both the cancellation and the correction should be recorded as succeeded actions");
        Verify.True(
            entries[1].ChainPosition > entries[0].ChainPosition,
            "The correction's audit entry should be appended after the cancellation's");

        var correctionEntry = await _flow.Auditor.EntryAsync(entries[1].Id);
        Verify.Ok(correctionEntry, "The Auditor should be able to read the correction's audit entry");
        Verify.Equal(
            entries[0].Id,
            correctionEntry.Value!.RelatedEntryId,
            "The correction's audit entry should link to the entry of the change it withdrew");

        var report = await _flow.Auditor.IntegrityAsync();
        Verify.Ok(report, "The Auditor should be able to check the audit chain after the correction");
        Verify.Equal(
            "intact",
            report.Value!.Status,
            "The audit chain should still verify after a correction that keeps both entries");
    }

    [FlowFact]
    public async Task Correcting_the_same_change_twice_is_a_conflict_and_leaves_one_correction()
    {
        // Arrange
        var journey = await _flow.Journeys.CreateAsync();
        var cancellation = await _flow.Changes.CancelAsync(journey);
        await _flow.Changes.CorrectAsync(cancellation.Id);

        // Act
        var second = await _flow.ControllerWrites(StaffDirectory.Controller)
            .PublishCorrectionAsync(cancellation.Id);

        // Assert
        Verify.Conflict(second, "A second correction of the same change should be refused");

        var guard = await _flow.Guard.JourneyViewAsync(journey.Id);
        Verify.Ok(guard, "The Guard's view should be readable after the refused second correction");
        Verify.Equal(
            2,
            guard.Value!.Changes.Count,
            "The refused second correction should leave exactly the cancellation and the one correction");
    }

    [FlowFact]
    public async Task Happy_path_the_provider_receives_the_cancellation_then_the_correction_that_references_it()
    {
        // Arrange
        var journey = await _flow.Journeys.CreateAsync();
        await using var provider = await ProviderReceiver.StartAsync();
        await _flow.Provider.RegisterWebhookAsync(provider.CallbackUrl);

        // Act
        var cancellation = await _flow.Changes.CancelAsync(journey);
        await _flow.Changes.CorrectAsync(cancellation.Id);

        // Assert
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
    }

    [FlowFact]
    public async Task Happy_path_the_provider_receives_one_journeys_changes_in_sequence_number_order()
    {
        // Arrange
        var journey = await _flow.Journeys.CreateAsync();
        await using var provider = await ProviderReceiver.StartAsync();
        await _flow.Provider.RegisterWebhookAsync(provider.CallbackUrl);

        // Act
        await _flow.Changes.DelayAsync(journey, fromStopSequence: 2, minutes: 5);
        var cancellation = await _flow.Changes.CancelAsync(journey);
        await _flow.Changes.CorrectAsync(cancellation.Id);

        // Assert
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
    }
}
