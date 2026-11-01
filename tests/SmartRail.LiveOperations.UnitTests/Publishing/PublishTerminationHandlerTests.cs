using SmartRail.LiveOperations.Application.Publishing.PublishTermination;
using SmartRail.LiveOperations.Contracts;
using SmartRail.LiveOperations.Domain.Changes;
using SmartRail.LiveOperations.UnitTests.TestDoubles;

namespace SmartRail.LiveOperations.UnitTests.Publishing;

public sealed class PublishTerminationHandlerTests
{
    [Fact]
    public async Task Happy_path_stores_and_audits_the_termination_applies_its_fact_and_returns_the_change()
    {
        // Arrange
        var state = TestData.RunningJourney();
        var driver = new PublisherTestDriver(state);
        var handler = new PublishTerminationCommandHandler(driver.CreatePublisher());
        var command = new PublishTerminationCommand(state.Id, 1);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        var change = Assert.Single(driver.Changes.Changes);
        Assert.Equal(StatusChangeType.Termination, change.Type);
        Assert.Equal(state.Id, change.JourneyId);
        Assert.Equal(1, change.StopSequence);
        Assert.Equal(driver.StaffId, change.PublishedBy);
        Assert.Equal(TestData.Now, change.PublishedAt);
        Assert.Equal(driver.Audit.AuditEntryId, change.AuditEntryId);

        var fact = Assert.Single(driver.Sender.Commands);
        Assert.Equal(JourneyEventType.TerminationPublished, fact.EventType);
        Assert.Equal(change.Id, fact.SourceId);

        var audit = Assert.Single(driver.Audit.Requests);
        Assert.Equal("PublishTermination", audit.Action);
        Assert.Equal("{\"status\":\"running\"}", audit.Before);
        Assert.Equal("{\"status\":\"terminated\",\"lastStopSequence\":1}", audit.After);

        Assert.Equal(change.Id, result.Value.Id);
        Assert.Equal("termination", result.Value.Type);
        Assert.Equal("1", result.Value.Values["lastStopSequence"]);
        Assert.Equal("Controller One", result.Value.PublisherName);
        Assert.Equal(1, driver.UnitOfWork.SaveChangesCount);
        Assert.Equal(1, driver.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task A_journey_that_has_not_started_cannot_be_terminated_and_nothing_is_written()
    {
        // Arrange
        var state = TestData.Journey();
        var driver = new PublisherTestDriver(state);
        var handler = new PublishTerminationCommandHandler(driver.CreatePublisher());

        // Act
        var result = await handler.Handle(
            new PublishTerminationCommand(state.Id, 1),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(StatusChangeErrors.TerminationNotStarted, result.Error);
        Assert.Empty(driver.Changes.Changes);
        Assert.Empty(driver.Sender.Commands);
        Assert.Empty(driver.Audit.Requests);
    }
}
