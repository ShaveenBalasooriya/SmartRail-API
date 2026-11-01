using SmartRail.LiveOperations.Application.Publishing.PublishDelay;
using SmartRail.LiveOperations.Contracts;
using SmartRail.LiveOperations.Domain.Changes;
using SmartRail.LiveOperations.UnitTests.TestDoubles;

namespace SmartRail.LiveOperations.UnitTests.Publishing;

public sealed class PublishDelayHandlerTests
{
    [Fact]
    public async Task Happy_path_stores_and_audits_the_delay_applies_its_fact_and_returns_the_change()
    {
        // Arrange
        var state = TestData.RunningJourney();
        var driver = new PublisherTestDriver(state);
        var handler = new PublishDelayCommandHandler(driver.CreatePublisher());
        var command = new PublishDelayCommand(state.Id, 2, 15);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        var change = Assert.Single(driver.Changes.Changes);
        Assert.Equal(StatusChangeType.Delay, change.Type);
        Assert.Equal(state.Id, change.JourneyId);
        Assert.Equal(2, change.StopSequence);
        Assert.Equal(15, change.DelayMinutes);
        Assert.Equal(driver.StaffId, change.PublishedBy);
        Assert.Equal(TestData.Now, change.PublishedAt);
        Assert.Equal(driver.Audit.AuditEntryId, change.AuditEntryId);

        var fact = Assert.Single(driver.Sender.Commands);
        Assert.Equal(JourneyEventType.DelayPublished, fact.EventType);
        Assert.Equal(change.Id, fact.SourceId);
        Assert.Equal(TestData.Now, fact.OccurredAt);
        Assert.Equal(TestData.Now, fact.RecordedAt);

        var audit = Assert.Single(driver.Audit.Requests);
        Assert.Equal("PublishDelay", audit.Action);
        Assert.Equal("{\"delayMinutes\":0}", audit.Before);
        Assert.Equal("{\"fromStopSequence\":2,\"delayMinutes\":15}", audit.After);

        Assert.Equal(change.Id, result.Value.Id);
        Assert.Equal("delay", result.Value.Type);
        Assert.Equal("2", result.Value.Values["fromStopSequence"]);
        Assert.Equal("15", result.Value.Values["minutes"]);
        Assert.Equal("Controller One", result.Value.PublisherName);
        Assert.Equal("automatically_published", result.Value.Provenance);
        Assert.False(result.Value.Corrected);
        Assert.Equal(1, driver.UnitOfWork.SaveChangesCount);
        Assert.Equal(1, driver.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task A_domain_validation_failure_is_passed_through_without_a_change_event_or_audit()
    {
        // Arrange
        var state = TestData.Journey();
        var driver = new PublisherTestDriver(state);
        var handler = new PublishDelayCommandHandler(driver.CreatePublisher());
        var command = new PublishDelayCommand(state.Id, 1, 0);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(StatusChangeErrors.DelayMinutesOutOfRange, result.Error);
        Assert.Empty(driver.Changes.Changes);
        Assert.Empty(driver.Sender.Commands);
        Assert.Empty(driver.Audit.Requests);
        Assert.Equal(0, driver.UnitOfWork.CommitCount);
    }
}
