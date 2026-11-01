using SmartRail.LiveOperations.Application.Abstractions;
using SmartRail.LiveOperations.Application.ControllerView;
using SmartRail.LiveOperations.Application.ControllerView.GetControllerJourney;
using SmartRail.LiveOperations.Contracts;
using SmartRail.LiveOperations.UnitTests.TestDoubles;
using SmartRail.SharedKernel.Results;

namespace SmartRail.LiveOperations.UnitTests.ControllerView;

public sealed class GetControllerJourneyHandlerTests
{
    [Fact]
    public async Task Happy_path_returns_the_journey_ordered_stops_and_named_change_history()
    {
        // Arrange
        var state = TestData.Journey();
        var publisherId = Guid.CreateVersion7();
        var change = new StatusChangeView(
            Guid.CreateVersion7(),
            "delay",
            new Dictionary<string, string?> { ["minutes"] = "10" },
            publisherId,
            TestData.Now,
            "automatically_published",
            false);
        var timetable = new StubTimetable();
        timetable.SetJourneys(state.ServiceDate, TestData.TimetableJourney(state));
        var network = new StubNetworkCatalog
        {
            StationsToReturn = GetControllerJourneysHandlerTests.StationsFor(state),
        };
        var staff = new StubStaffDirectory(new Dictionary<Guid, string>
        {
            [state.GuardStaffId] = "Guard One",
            [publisherId] = "Controller One",
        });
        var changes = new StubStatusChangeReader { ChangesToReturn = [change] };
        var handler = new GetControllerJourneyQueryHandler(
            new FakeLiveJourneyRepository(state),
            changes,
            timetable,
            network,
            staff,
            new OperationsOptions(10, 60),
            new StubTimeProvider(TestData.Now));

        // Act
        var result = await handler.Handle(new GetControllerJourneyQuery(state.Id), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(state.Id, result.Value.Journey.Id);
        Assert.Equal("Coastal Express", result.Value.Journey.ServiceName);
        Assert.Equal("Guard One", result.Value.Journey.GuardName);
        Assert.Equal(3, result.Value.Stops.Count);
        Assert.Equal([1, 2, 3], result.Value.Stops.Select(stop => stop.Sequence));
        Assert.Equal("Station 1", result.Value.Stops[0].Station?.Name);
        Assert.Equal("1", result.Value.Stops[0].Platform?.Number);
        var history = Assert.Single(result.Value.Changes);
        Assert.Equal(change.Id, history.Id);
        Assert.Equal("Controller One", history.PublisherName);
        Assert.Equal(state.Id, Assert.Single(changes.RequestedJourneyIds));
    }

    [Fact]
    public async Task A_missing_journey_returns_not_found_without_reading_dependent_views()
    {
        // Arrange
        var timetable = new StubTimetable();
        var changes = new StubStatusChangeReader();
        var handler = new GetControllerJourneyQueryHandler(
            new FakeLiveJourneyRepository(),
            changes,
            timetable,
            new StubNetworkCatalog(),
            new StubStaffDirectory(),
            new OperationsOptions(10, 60),
            new StubTimeProvider(TestData.Now));
        var journeyId = Guid.CreateVersion7();

        // Act
        var result = await handler.Handle(new GetControllerJourneyQuery(journeyId), CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Empty(timetable.RequestedDates);
        Assert.Empty(changes.RequestedJourneyIds);
    }

    [Fact]
    public async Task A_change_history_failure_is_propagated()
    {
        // Arrange
        var state = TestData.Journey();
        var changes = new StubStatusChangeReader
        {
            ExceptionToThrow = new InvalidOperationException("History unavailable."),
        };
        var handler = new GetControllerJourneyQueryHandler(
            new FakeLiveJourneyRepository(state),
            changes,
            new StubTimetable(),
            new StubNetworkCatalog(),
            new StubStaffDirectory(),
            new OperationsOptions(10, 60),
            new StubTimeProvider(TestData.Now));

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new GetControllerJourneyQuery(state.Id), CancellationToken.None));

        // Assert
        Assert.Equal("History unavailable.", exception.Message);
    }
}
