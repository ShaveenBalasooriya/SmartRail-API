using SmartRail.LiveOperations.Application.Abstractions;
using SmartRail.LiveOperations.Application.ControllerView.GetControllerJourneys;
using SmartRail.LiveOperations.Contracts;
using SmartRail.LiveOperations.UnitTests.TestDoubles;
using SmartRail.RailNetwork.Contracts;

namespace SmartRail.LiveOperations.UnitTests.ControllerView;

public sealed class GetControllerJourneysHandlerTests
{
    [Fact]
    public async Task Happy_path_returns_todays_journeys_with_timetable_station_and_guard_names()
    {
        // Arrange
        var state = TestData.RunningJourney();
        var timetable = new StubTimetable();
        timetable.SetJourneys(state.ServiceDate, TestData.TimetableJourney(state));
        var network = new StubNetworkCatalog { StationsToReturn = StationsFor(state) };
        var staff = new StubStaffDirectory(new Dictionary<Guid, string>
        {
            [state.GuardStaffId] = "Guard One",
        });
        var handler = new GetControllerJourneysQueryHandler(
            new FakeLiveJourneyRepository(state),
            timetable,
            network,
            staff,
            new StubRailwayCalendar(),
            new OperationsOptions(1_000, 60),
            new StubTimeProvider(TestData.Now));

        // Act
        var result = await handler.Handle(new GetControllerJourneysQuery(), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        var journey = Assert.Single(result.Value);
        Assert.Equal(state.Id, journey.Id);
        Assert.Equal("Coastal Express", journey.ServiceName);
        Assert.Equal("COAST", journey.LineCode);
        Assert.Equal("Coastal Line", journey.LineName);
        Assert.Equal(ServiceDirection.Outbound, journey.Direction);
        Assert.Equal(JourneyLifecycleStatus.Running, journey.Status);
        Assert.Equal("Guard One", journey.GuardName);
        Assert.Equal("Station 1", journey.LastDepartedStation?.Name);
        Assert.Equal("Station 2", journey.NextStation?.Name);
        Assert.False(journey.IsStale);
        Assert.Equal(state.ServiceDate, Assert.Single(timetable.RequestedDates));
    }

    [Fact]
    public async Task A_running_journey_older_than_the_stale_threshold_is_flagged()
    {
        // Arrange
        var state = TestData.RunningJourney();
        var handler = new GetControllerJourneysQueryHandler(
            new FakeLiveJourneyRepository(state),
            new StubTimetable(),
            new StubNetworkCatalog(),
            new StubStaffDirectory(),
            new StubRailwayCalendar(),
            new OperationsOptions(10, 60),
            new StubTimeProvider(TestData.Now));

        // Act
        var result = await handler.Handle(new GetControllerJourneysQuery(), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(Assert.Single(result.Value).IsStale);
    }

    [Fact]
    public async Task No_live_journeys_returns_an_empty_list()
    {
        // Arrange
        var handler = new GetControllerJourneysQueryHandler(
            new FakeLiveJourneyRepository(),
            new StubTimetable(),
            new StubNetworkCatalog(),
            new StubStaffDirectory(),
            new StubRailwayCalendar(),
            new OperationsOptions(10, 60),
            new StubTimeProvider(TestData.Now));

        // Act
        var result = await handler.Handle(new GetControllerJourneysQuery(), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task A_timetable_failure_is_propagated()
    {
        // Arrange
        var timetable = new StubTimetable
        {
            ExceptionToThrow = new InvalidOperationException("Timetable unavailable."),
        };
        var handler = new GetControllerJourneysQueryHandler(
            new FakeLiveJourneyRepository(TestData.Journey()),
            timetable,
            new StubNetworkCatalog(),
            new StubStaffDirectory(),
            new StubRailwayCalendar(),
            new OperationsOptions(10, 60),
            new StubTimeProvider(TestData.Now));

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new GetControllerJourneysQuery(), CancellationToken.None));

        // Assert
        Assert.Equal("Timetable unavailable.", exception.Message);
    }

    internal static IReadOnlyList<StationDto> StationsFor(Domain.LiveState.JourneyLiveState state) =>
        [.. state.Stops
            .OrderBy(stop => stop.Sequence)
            .Select(stop => new StationDto(
                stop.StationId,
                $"S{stop.Sequence}",
                $"Station {stop.Sequence}",
                false,
                stop.PlatformId is { } platformId
                    ? [new PlatformDto(platformId, stop.StationId, stop.Sequence.ToString())]
                    : [],
                []))];
}
