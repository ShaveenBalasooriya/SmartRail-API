using SmartRail.LiveOperations.Application.Publishing;
using SmartRail.LiveOperations.Domain.Changes;
using SmartRail.LiveOperations.Domain.LiveState;
using SmartRail.LiveOperations.UnitTests.TestDoubles;
using SmartRail.SharedKernel.Results;
using JourneyEventType = SmartRail.LiveOperations.Contracts.JourneyEventType;

namespace SmartRail.LiveOperations.UnitTests.Publishing;

public sealed class StatusChangePublisherTests
{
    [Fact]
    public async Task Happy_path_runs_the_write_pipeline_in_order_and_commits_once()
    {
        // Arrange
        var state = TestData.Journey();
        var driver = new PublisherTestDriver(state);
        var publisher = driver.CreatePublisher();

        // Act
        var result = await publisher.PublishAsync(
            state.Id,
            "PublishDelay",
            JourneyEventType.DelayPublished,
            (lockedState, staffId, publishedAt, _) =>
            {
                Assert.Same(state, lockedState);
                Assert.Equal(driver.StaffId, staffId);
                Assert.Equal(TestData.Now, publishedAt);
                return Task.FromResult(Result.Success(PublisherTestDriver.DelayPlan(state, staffId)));
            },
            CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        var change = Assert.Single(driver.Changes.Changes);
        Assert.Equal(driver.Audit.AuditEntryId, change.AuditEntryId);
        Assert.Single(driver.Sender.Commands);
        Assert.Single(driver.Audit.Requests);
        Assert.Equal(1, driver.UnitOfWork.SaveChangesCount);
        Assert.Equal(1, driver.UnitOfWork.CommitCount);
        Assert.Equal(0, driver.UnitOfWork.RollbackCount);
        Assert.Equal(change.Id, result.Value.Id);
    }

    [Fact]
    public async Task A_missing_journey_returns_not_found_without_calling_the_change_plan()
    {
        // Arrange
        var driver = new PublisherTestDriver(state: null);
        var planCalled = false;

        // Act
        var result = await driver.CreatePublisher().PublishAsync(
            Guid.CreateVersion7(),
            "PublishDelay",
            JourneyEventType.DelayPublished,
            (_, _, _, _) =>
            {
                planCalled = true;
                throw new InvalidOperationException("The plan must not run.");
            },
            CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.False(planCalled);
        Assert.Empty(driver.Changes.Changes);
        Assert.Empty(driver.Audit.Requests);
    }

    [Fact]
    public async Task A_journey_from_another_service_day_is_rejected_before_the_change_plan()
    {
        // Arrange
        var state = TestData.Journey(serviceDate: DateOnly.FromDateTime(TestData.Now.DateTime).AddDays(-1));
        var driver = new PublisherTestDriver(state);
        var planCalled = false;

        // Act
        var result = await driver.CreatePublisher().PublishAsync(
            state.Id,
            "PublishDelay",
            JourneyEventType.DelayPublished,
            (_, _, _, _) =>
            {
                planCalled = true;
                throw new InvalidOperationException("The plan must not run.");
            },
            CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(StatusChangeErrors.JourneyNotToday, result.Error);
        Assert.False(planCalled);
        Assert.Empty(driver.Changes.Changes);
    }

    [Fact]
    public async Task A_closed_journey_is_rejected_by_the_default_lifecycle_rule()
    {
        // Arrange
        var state = TestData.CancelledJourney();
        var driver = new PublisherTestDriver(state);

        // Act
        var result = await driver.CreatePublisher().PublishAsync(
            state.Id,
            "PublishDelay",
            JourneyEventType.DelayPublished,
            (lockedState, staffId, _, _) =>
                Task.FromResult(Result.Success(PublisherTestDriver.DelayPlan(lockedState, staffId))),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(StatusChangeErrors.JourneyClosed, result.Error);
        Assert.Empty(driver.Changes.Changes);
        Assert.Empty(driver.Sender.Commands);
    }

    [Fact]
    public async Task An_explicit_lifecycle_window_allows_a_correction_on_a_cancelled_journey()
    {
        // Arrange
        var state = TestData.CancelledJourney();
        var driver = new PublisherTestDriver(state);

        // Act
        var result = await driver.CreatePublisher().PublishAsync(
            state.Id,
            "PublishCorrection",
            JourneyEventType.CorrectionPublished,
            (lockedState, staffId, _, _) =>
                Task.FromResult(Result.Success(PublisherTestDriver.DelayPlan(lockedState, staffId))),
            CancellationToken.None,
            acceptedStatuses: [JourneyLifecycleStatus.Cancelled]);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Single(driver.Changes.Changes);
        Assert.Single(driver.Audit.Requests);
        Assert.Equal(1, driver.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task A_change_plan_failure_is_returned_without_storing_or_auditing_anything()
    {
        // Arrange
        var state = TestData.Journey();
        var driver = new PublisherTestDriver(state);
        var failure = StatusChangeErrors.DelayMinutesOutOfRange;

        // Act
        var result = await driver.CreatePublisher().PublishAsync(
            state.Id,
            "PublishDelay",
            JourneyEventType.DelayPublished,
            (_, _, _, _) => Task.FromResult(Result.Failure<StatusChangePlan>(failure)),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(failure, result.Error);
        Assert.Empty(driver.Changes.Changes);
        Assert.Empty(driver.Sender.Commands);
        Assert.Empty(driver.Audit.Requests);
    }

    [Fact]
    public async Task A_duplicate_correction_from_the_repository_is_a_conflict_and_stops_the_pipeline()
    {
        // Arrange
        var state = TestData.Journey();
        var driver = new PublisherTestDriver(state);
        driver.Changes.AddResult = false;

        // Act
        var result = await driver.CreatePublisher().PublishAsync(
            state.Id,
            "PublishCorrection",
            JourneyEventType.CorrectionPublished,
            (lockedState, staffId, _, _) =>
                Task.FromResult(Result.Success(PublisherTestDriver.DelayPlan(lockedState, staffId))),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(StatusChangeErrors.ChangeAlreadyCorrected, result.Error);
        Assert.Empty(driver.Changes.Changes);
        Assert.Empty(driver.Sender.Commands);
        Assert.Empty(driver.Audit.Requests);
        Assert.Equal(0, driver.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task A_failed_fact_application_rolls_back_the_change_and_skips_the_audit()
    {
        // Arrange
        var state = TestData.Journey();
        var driver = new PublisherTestDriver(state);
        var failure = new Error("Test.FactFailed", "The fact could not be applied.");
        driver.Sender.FailureToReturn = failure;

        // Act
        var result = await driver.CreatePublisher().PublishAsync(
            state.Id,
            "PublishDelay",
            JourneyEventType.DelayPublished,
            (lockedState, staffId, _, _) =>
                Task.FromResult(Result.Success(PublisherTestDriver.DelayPlan(lockedState, staffId))),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(failure, result.Error);
        Assert.Empty(driver.Changes.Changes);
        Assert.Empty(driver.Audit.Requests);
        Assert.Equal(0, driver.UnitOfWork.SaveChangesCount);
        Assert.Equal(0, driver.UnitOfWork.CommitCount);
        Assert.Equal(1, driver.UnitOfWork.RollbackCount);
    }

    [Fact]
    public async Task An_audit_failure_is_propagated_and_the_stored_change_is_rolled_back()
    {
        // Arrange
        var state = TestData.Journey();
        var driver = new PublisherTestDriver(state);
        driver.Audit.ExceptionToThrow = new InvalidOperationException("Audit unavailable.");

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            driver.CreatePublisher().PublishAsync(
                state.Id,
                "PublishDelay",
                JourneyEventType.DelayPublished,
                (lockedState, staffId, _, _) =>
                    Task.FromResult(Result.Success(PublisherTestDriver.DelayPlan(lockedState, staffId))),
                CancellationToken.None));

        // Assert
        Assert.Equal("Audit unavailable.", exception.Message);
        Assert.Empty(driver.Changes.Changes);
        Assert.Single(driver.Sender.Commands);
        Assert.Single(driver.Audit.Requests);
        Assert.Equal(0, driver.UnitOfWork.SaveChangesCount);
        Assert.Equal(0, driver.UnitOfWork.CommitCount);
        Assert.Equal(1, driver.UnitOfWork.RollbackCount);
    }

    [Fact]
    public async Task An_unauthenticated_caller_cannot_start_a_publish_transaction()
    {
        // Arrange
        var state = TestData.Journey();
        var driver = new PublisherTestDriver(state);
        driver.CurrentUser = new StubCurrentUser(null);

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            driver.CreatePublisher().PublishAsync(
                state.Id,
                "PublishDelay",
                JourneyEventType.DelayPublished,
                (lockedState, staffId, _, _) =>
                    Task.FromResult(Result.Success(PublisherTestDriver.DelayPlan(lockedState, staffId))),
                CancellationToken.None));

        // Assert
        Assert.Contains("authenticated staff member", exception.Message);
        Assert.Equal(0, driver.UnitOfWork.BeginTransactionCount);
        Assert.Empty(driver.Changes.Changes);
    }
}
