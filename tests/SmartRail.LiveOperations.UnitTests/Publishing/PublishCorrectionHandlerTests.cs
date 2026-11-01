using SmartRail.LiveOperations.Application.Publishing.PublishCorrection;
using SmartRail.LiveOperations.Domain.Calculation;
using SmartRail.LiveOperations.Domain.Changes;
using SmartRail.LiveOperations.Domain.LiveState;
using SmartRail.LiveOperations.UnitTests.TestDoubles;
using SmartRail.RailNetwork.Contracts;
using JourneyEventType = SmartRail.LiveOperations.Contracts.JourneyEventType;

namespace SmartRail.LiveOperations.UnitTests.Publishing;

public sealed class PublishCorrectionHandlerTests
{
    [Fact]
    public async Task Happy_path_corrects_a_delay_and_links_the_audit_to_the_original_entry()
    {
        // Arrange
        var state = TestData.Journey();
        state.Apply([DelayFact(corrected: false)], TestData.Now.AddMinutes(-1));
        var original = TestData.Delay(state.Id, Guid.CreateVersion7(), minutes: 15);
        var originalAuditEntryId = Guid.CreateVersion7();
        original.AttachAuditEntry(originalAuditEntryId);
        var driver = new PublisherTestDriver(state, original);
        driver.Sender.OnSend = _ =>
            state.Apply([DelayFact(corrected: true)], TestData.Now);
        var handler = new PublishCorrectionCommandHandler(
            driver.CreatePublisher(),
            driver.Changes,
            new StubNetworkCatalog());

        // Act
        var result = await handler.Handle(new PublishCorrectionCommand(original.Id), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        var correction = Assert.Single(driver.Changes.Changes.Where(change => change.Type == StatusChangeType.Correction));
        Assert.Equal(original.Id, correction.CorrectsChangeId);
        Assert.Equal(driver.StaffId, correction.PublishedBy);
        Assert.Equal(driver.Audit.AuditEntryId, correction.AuditEntryId);

        var fact = Assert.Single(driver.Sender.Commands);
        Assert.Equal(JourneyEventType.CorrectionPublished, fact.EventType);
        Assert.Equal(correction.Id, fact.SourceId);

        var audit = Assert.Single(driver.Audit.Requests);
        Assert.Equal("PublishCorrection", audit.Action);
        Assert.Equal("{\"delayMinutes\":15}", audit.Before);
        Assert.Equal("{\"delayMinutes\":0}", audit.After);
        Assert.Equal(originalAuditEntryId, audit.RelatedEntryId);
        Assert.Equal("correction", result.Value.Type);
        Assert.Equal(original.Id.ToString(), result.Value.Values["correctsChangeId"]);
        Assert.Equal(1, driver.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task A_wrong_cancellation_can_be_corrected_on_the_closed_journey_it_left_behind()
    {
        // Arrange
        var state = TestData.CancelledJourney();
        var original = TestData.Cancellation(state.Id, Guid.CreateVersion7());
        var driver = new PublisherTestDriver(state, original);
        driver.Sender.OnSend = _ => state.Apply(
            [new ChangeFact(ChangeKind.Cancellation, null, null, null, TestData.Now.AddMinutes(-1), 1, true)],
            TestData.Now);
        var handler = NewHandler(driver);

        // Act
        var result = await handler.Handle(new PublishCorrectionCommand(original.Id), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        var audit = Assert.Single(driver.Audit.Requests);
        Assert.Equal("{\"status\":\"cancelled\"}", audit.Before);
        Assert.Equal("{\"status\":\"scheduled\"}", audit.After);
        Assert.Equal(JourneyLifecycleStatus.Scheduled, state.Status);
        Assert.Equal(2, driver.Changes.Changes.Count);
    }

    [Fact]
    public async Task A_wrong_termination_can_be_corrected_and_the_audit_records_the_restored_running_status()
    {
        // Arrange
        var state = TestData.TerminatedJourney();
        var original = TestData.Termination(state.Id, Guid.CreateVersion7());
        var driver = new PublisherTestDriver(state, original);
        driver.Sender.OnSend = _ => state.Apply(
            [
                new DepartureFact(1, TestData.Now.AddHours(-2), TestData.Now.AddHours(-2).AddMinutes(1), 1),
                new ChangeFact(ChangeKind.Termination, 1, null, null, TestData.Now.AddMinutes(-1), 2, true),
            ],
            TestData.Now);
        var handler = NewHandler(driver);

        // Act
        var result = await handler.Handle(new PublishCorrectionCommand(original.Id), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        var audit = Assert.Single(driver.Audit.Requests);
        Assert.Equal("{\"status\":\"terminated\",\"lastStopSequence\":1}", audit.Before);
        Assert.Equal("{\"status\":\"running\"}", audit.After);
        Assert.Equal(JourneyLifecycleStatus.Running, state.Status);
    }

    [Fact]
    public async Task A_platform_correction_uses_passenger_platform_numbers_before_and_after()
    {
        // Arrange
        var stationId = Guid.CreateVersion7();
        var previousPlatform = new PlatformDto(Guid.CreateVersion7(), stationId, "1");
        var newPlatform = new PlatformDto(Guid.CreateVersion7(), stationId, "2");
        var state = TestData.Journey(stationId: stationId, platformId: previousPlatform.Id);
        var original = TestData.PlatformChange(
            state.Id,
            Guid.CreateVersion7(),
            newPlatformId: newPlatform.Id,
            previousPlatformId: previousPlatform.Id);
        var driver = new PublisherTestDriver(state, original);
        var handler = new PublishCorrectionCommandHandler(
            driver.CreatePublisher(),
            driver.Changes,
            new StubNetworkCatalog(previousPlatform, newPlatform));

        // Act
        var result = await handler.Handle(new PublishCorrectionCommand(original.Id), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        var audit = Assert.Single(driver.Audit.Requests);
        Assert.Equal("{\"platform\":\"2\"}", audit.Before);
        Assert.Equal("{\"platform\":\"1\"}", audit.After);
        Assert.Equal(previousPlatform.Id, state.StopAt(1)!.PlatformId);
    }

    // DEF-007: the stored stop already holds the changed platform, so the recomputation has to
    // recover the platform the withdrawn change replaced rather than keep the stored one.
    [Fact]
    public async Task A_platform_correction_restores_the_original_platform_on_the_stored_stop()
    {
        // Arrange
        var stationId = Guid.CreateVersion7();
        var previousPlatform = new PlatformDto(Guid.CreateVersion7(), stationId, "1");
        var newPlatform = new PlatformDto(Guid.CreateVersion7(), stationId, "2");
        var state = TestData.Journey(stationId: stationId, platformId: newPlatform.Id);
        var original = TestData.PlatformChange(
            state.Id,
            Guid.CreateVersion7(),
            newPlatformId: newPlatform.Id,
            previousPlatformId: previousPlatform.Id);
        var driver = new PublisherTestDriver(state, original);

        // The fact sink recomputes from the stored facts, where the original change is now corrected.
        driver.Sender.OnSend = _ => state.Apply(
            [new ChangeFact(
                ChangeKind.PlatformChange,
                1,
                null,
                newPlatform.Id,
                TestData.Now.AddMinutes(-1),
                1,
                Corrected: true,
                PreviousPlatformId: previousPlatform.Id)],
            TestData.Now);

        var handler = new PublishCorrectionCommandHandler(
            driver.CreatePublisher(),
            driver.Changes,
            new StubNetworkCatalog(previousPlatform, newPlatform));

        // Act
        var result = await handler.Handle(new PublishCorrectionCommand(original.Id), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        var audit = Assert.Single(driver.Audit.Requests);
        Assert.Equal("{\"platform\":\"2\"}", audit.Before);
        Assert.Equal("{\"platform\":\"1\"}", audit.After);
        Assert.Equal(previousPlatform.Id, state.StopAt(1)!.PlatformId);
    }

    [Fact]
    public async Task An_unknown_correction_target_returns_not_found_before_opening_a_transaction()
    {
        // Arrange
        var driver = new PublisherTestDriver(TestData.Journey());
        var handler = NewHandler(driver);

        // Act
        var result = await handler.Handle(
            new PublishCorrectionCommand(Guid.CreateVersion7()),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(StatusChangeErrors.CorrectionTargetNotFound, result.Error);
        Assert.Equal(0, driver.UnitOfWork.BeginTransactionCount);
        Assert.Empty(driver.Audit.Requests);
    }

    [Fact]
    public async Task A_correction_cannot_itself_be_corrected()
    {
        // Arrange
        var state = TestData.Journey();
        var original = TestData.Delay(state.Id, Guid.CreateVersion7());
        var correction = TestData.Correction(state.Id, original.Id, Guid.CreateVersion7());
        var driver = new PublisherTestDriver(state, original, correction);
        var handler = NewHandler(driver);

        // Act
        var result = await handler.Handle(new PublishCorrectionCommand(correction.Id), CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(StatusChangeErrors.CorrectionOfCorrection, result.Error);
        Assert.Equal(2, driver.Changes.Changes.Count);
        Assert.Empty(driver.Sender.Commands);
        Assert.Empty(driver.Audit.Requests);
    }

    [Fact]
    public async Task A_change_that_has_already_been_corrected_returns_conflict_without_a_second_correction()
    {
        // Arrange
        var state = TestData.Journey();
        var original = TestData.Delay(state.Id, Guid.CreateVersion7());
        var existingCorrection = TestData.Correction(state.Id, original.Id, Guid.CreateVersion7());
        var driver = new PublisherTestDriver(state, original, existingCorrection);
        var handler = NewHandler(driver);

        // Act
        var result = await handler.Handle(new PublishCorrectionCommand(original.Id), CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(StatusChangeErrors.ChangeAlreadyCorrected, result.Error);
        Assert.Equal(2, driver.Changes.Changes.Count);
        Assert.Empty(driver.Sender.Commands);
        Assert.Empty(driver.Audit.Requests);
    }

    [Fact]
    public async Task A_platform_correction_for_a_stop_not_on_the_journey_fails_without_writes()
    {
        // Arrange
        var state = TestData.Journey();
        var original = TestData.PlatformChange(
            state.Id,
            Guid.CreateVersion7(),
            stopSequence: 99);
        var driver = new PublisherTestDriver(state, original);
        var handler = NewHandler(driver);

        // Act
        var result = await handler.Handle(new PublishCorrectionCommand(original.Id), CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(StatusChangeErrors.PlatformStopNotCalling, result.Error);
        Assert.Single(driver.Changes.Changes);
        Assert.Empty(driver.Sender.Commands);
        Assert.Empty(driver.Audit.Requests);
    }

    private static PublishCorrectionCommandHandler NewHandler(PublisherTestDriver driver) =>
        new(driver.CreatePublisher(), driver.Changes, new StubNetworkCatalog());

    private static ChangeFact DelayFact(bool corrected) =>
        new(ChangeKind.Delay, 1, 15, null, TestData.Now.AddMinutes(-1), 1, corrected);

}
