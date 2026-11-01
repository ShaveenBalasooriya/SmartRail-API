using SmartRail.LiveOperations.Application.Publishing.PublishCancellation;
using SmartRail.LiveOperations.Contracts;
using SmartRail.LiveOperations.Domain.Changes;
using SmartRail.LiveOperations.UnitTests.TestDoubles;

namespace SmartRail.LiveOperations.UnitTests.Publishing;

public sealed class PublishCancellationHandlerTests
{
    [Fact]
    public async Task Happy_path_stores_and_audits_the_cancellation_applies_its_fact_and_returns_the_change()
    {
        // Arrange
        var state = TestData.Journey();
        var driver = new PublisherTestDriver(state);
        var handler = new PublishCancellationCommandHandler(driver.CreatePublisher());

        // Act
        var result = await handler.Handle(
            new PublishCancellationCommand(state.Id),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        var change = Assert.Single(driver.Changes.Changes);
        Assert.Equal(StatusChangeType.Cancellation, change.Type);
        Assert.Equal(state.Id, change.JourneyId);
        Assert.Equal(driver.StaffId, change.PublishedBy);
        Assert.Equal(TestData.Now, change.PublishedAt);
        Assert.Equal(driver.Audit.AuditEntryId, change.AuditEntryId);

        var fact = Assert.Single(driver.Sender.Commands);
        Assert.Equal(JourneyEventType.CancellationPublished, fact.EventType);
        Assert.Equal(change.Id, fact.SourceId);

        var audit = Assert.Single(driver.Audit.Requests);
        Assert.Equal("PublishCancellation", audit.Action);
        Assert.Equal("{\"status\":\"scheduled\"}", audit.Before);
        Assert.Equal("{\"status\":\"cancelled\"}", audit.After);

        Assert.Equal(change.Id, result.Value.Id);
        Assert.Equal("cancellation", result.Value.Type);
        Assert.Empty(result.Value.Values);
        Assert.Equal("Controller One", result.Value.PublisherName);
        Assert.Equal(1, driver.UnitOfWork.SaveChangesCount);
        Assert.Equal(1, driver.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task A_cancellation_after_departure_conflicts_without_a_change_event_or_audit()
    {
        // Arrange
        var state = TestData.RunningJourney();
        var driver = new PublisherTestDriver(state);
        var handler = new PublishCancellationCommandHandler(driver.CreatePublisher());

        // Act
        var result = await handler.Handle(
            new PublishCancellationCommand(state.Id),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(StatusChangeErrors.CancellationAfterDeparture, result.Error);
        Assert.Empty(driver.Changes.Changes);
        Assert.Empty(driver.Sender.Commands);
        Assert.Empty(driver.Audit.Requests);
    }
}
