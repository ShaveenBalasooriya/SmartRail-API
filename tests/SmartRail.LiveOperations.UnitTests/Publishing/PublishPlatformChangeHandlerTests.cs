using SmartRail.LiveOperations.Application.Publishing.PublishPlatformChange;
using SmartRail.LiveOperations.Contracts;
using SmartRail.LiveOperations.Domain.Changes;
using SmartRail.LiveOperations.UnitTests.TestDoubles;
using SmartRail.RailNetwork.Contracts;

namespace SmartRail.LiveOperations.UnitTests.Publishing;

public sealed class PublishPlatformChangeHandlerTests
{
    [Fact]
    public async Task Happy_path_stores_and_audits_the_platform_change_applies_its_fact_and_returns_the_change()
    {
        // Arrange
        var stationId = Guid.CreateVersion7();
        var previousPlatform = new PlatformDto(Guid.CreateVersion7(), stationId, "1");
        var newPlatform = new PlatformDto(Guid.CreateVersion7(), stationId, "2");
        var state = TestData.Journey(stationId: stationId, platformId: previousPlatform.Id);
        var driver = new PublisherTestDriver(state);
        var network = new StubNetworkCatalog(previousPlatform, newPlatform);
        var handler = new PublishPlatformChangeCommandHandler(driver.CreatePublisher(), network);
        var command = new PublishPlatformChangeCommand(state.Id, 1, newPlatform.Id);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        var change = Assert.Single(driver.Changes.Changes);
        Assert.Equal(StatusChangeType.PlatformChange, change.Type);
        Assert.Equal(state.Id, change.JourneyId);
        Assert.Equal(1, change.StopSequence);
        Assert.Equal(previousPlatform.Id, change.PreviousPlatformId);
        Assert.Equal(newPlatform.Id, change.NewPlatformId);
        Assert.Equal(driver.StaffId, change.PublishedBy);
        Assert.Equal(TestData.Now, change.PublishedAt);
        Assert.Equal(driver.Audit.AuditEntryId, change.AuditEntryId);
        Assert.Equal(stationId, Assert.Single(network.RequestedStationIds));

        var fact = Assert.Single(driver.Sender.Commands);
        Assert.Equal(JourneyEventType.PlatformChanged, fact.EventType);
        Assert.Equal(change.Id, fact.SourceId);

        var audit = Assert.Single(driver.Audit.Requests);
        Assert.Equal("PublishPlatformChange", audit.Action);
        Assert.Equal("{\"platform\":\"1\"}", audit.Before);
        Assert.Equal("{\"platform\":\"2\"}", audit.After);

        Assert.Equal(change.Id, result.Value.Id);
        Assert.Equal("platform_change", result.Value.Type);
        Assert.Equal("1", result.Value.Values["stopSequence"]);
        Assert.Equal(previousPlatform.Id.ToString(), result.Value.Values["previousPlatformId"]);
        Assert.Equal(newPlatform.Id.ToString(), result.Value.Values["platformId"]);
        Assert.Equal("Controller One", result.Value.PublisherName);
        Assert.Equal(1, driver.UnitOfWork.SaveChangesCount);
        Assert.Equal(1, driver.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task A_platform_from_another_station_is_rejected_without_a_change_event_or_audit()
    {
        // Arrange
        var stationId = Guid.CreateVersion7();
        var previousPlatform = new PlatformDto(Guid.CreateVersion7(), stationId, "1");
        var state = TestData.Journey(stationId: stationId, platformId: previousPlatform.Id);
        var driver = new PublisherTestDriver(state);
        var network = new StubNetworkCatalog(previousPlatform);
        var handler = new PublishPlatformChangeCommandHandler(driver.CreatePublisher(), network);
        var command = new PublishPlatformChangeCommand(state.Id, 1, Guid.CreateVersion7());

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(StatusChangeErrors.PlatformNotAtStation, result.Error);
        Assert.Empty(driver.Changes.Changes);
        Assert.Empty(driver.Sender.Commands);
        Assert.Empty(driver.Audit.Requests);
    }

    [Fact]
    public async Task An_unknown_stop_is_rejected_before_the_network_catalogue_is_called()
    {
        // Arrange
        var state = TestData.Journey();
        var driver = new PublisherTestDriver(state);
        var network = new StubNetworkCatalog();
        var handler = new PublishPlatformChangeCommandHandler(driver.CreatePublisher(), network);
        var command = new PublishPlatformChangeCommand(state.Id, 99, Guid.CreateVersion7());

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(StatusChangeErrors.PlatformStopNotCalling, result.Error);
        Assert.Empty(network.RequestedStationIds);
        Assert.Empty(driver.Changes.Changes);
        Assert.Empty(driver.Audit.Requests);
    }
}
