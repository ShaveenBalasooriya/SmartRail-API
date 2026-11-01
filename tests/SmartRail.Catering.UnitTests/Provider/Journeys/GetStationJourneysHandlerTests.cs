using SmartRail.Catering.Application.Provider.Journeys;
using SmartRail.Catering.Application.Provider.Journeys.GetStationJourneys;
using SmartRail.Catering.UnitTests.TestDoubles;
using SmartRail.LiveOperations.Contracts;
using SmartRail.RailNetwork.Contracts;
using SmartRail.SharedKernel.Results;

namespace SmartRail.Catering.UnitTests.Provider.Journeys;

public sealed class GetStationJourneysHandlerTests
{
    private static readonly TimeZoneInfo RailwayTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Colombo");

    [Fact]
    public async Task Happy_path_returns_only_calling_journeys_ordered_by_expected_arrival()
    {
        // Arrange
        var stationId = Guid.CreateVersion7();
        var destinationId = Guid.CreateVersion7();
        var platformId = Guid.CreateVersion7();
        var lineId = Guid.CreateVersion7();
        var serviceId = Guid.CreateVersion7();
        var serviceDate = DateOnly.FromDateTime(TestData.Now.DateTime);
        var station = TestData.Station(stationId, platformId, lineId);
        var destination = TestData.Station(destinationId, Guid.CreateVersion7(), lineId, "Galle");
        var network = new StubNetworkCatalog([station, destination], [TestData.Line(lineId)]);
        var timetable = new StubTimetable(TestData.Service(serviceId, lineId));

        var later = JourneyCallingAt(
            stationId,
            destinationId,
            platformId,
            lineId,
            serviceId,
            serviceDate,
            TestData.Now.AddHours(1),
            TestData.Now.AddHours(2));
        var earlier = JourneyCallingAt(
            stationId,
            destinationId,
            platformId,
            lineId,
            serviceId,
            serviceDate,
            TestData.Now.AddMinutes(30),
            TestData.Now.AddMinutes(45));
        var passing = JourneyCallingAt(
            stationId,
            destinationId,
            platformId,
            lineId,
            serviceId,
            serviceDate,
            TestData.Now,
            TestData.Now) with
        {
            Stops =
            [
                TestData.Stop(1, stationId, TestData.Now, isCalling: false),
                TestData.Stop(2, destinationId, TestData.Now.AddHours(1)),
            ],
        };
        var liveJourneys = new StubLiveJourneys(later, passing, earlier);
        var handler = HandlerDriver.CreateQueryHandler<GetStationJourneysQuery, IReadOnlyList<ProviderJourneyResponse>>(
            network,
            timetable,
            liveJourneys,
            new StubTimeProvider(TestData.Now),
            RailwayTimeZone);

        // Act
        var result = await handler.Handle(
            new GetStationJourneysQuery(stationId, serviceDate),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.Equal(earlier.JourneyId, result.Value[0].JourneyId);
        Assert.Equal(later.JourneyId, result.Value[1].JourneyId);

        var response = result.Value[0];
        Assert.Equal(lineId, response.Line.Id);
        Assert.Equal("Main Line", response.Line.Name);
        Assert.Equal(ServiceDirection.Outbound, response.Direction);
        Assert.Equal(destinationId, response.FinalDestination.Id);
        Assert.Equal("Galle", response.FinalDestination.Name);
        Assert.Equal(earlier.Stops[0].ScheduledArrival, response.ScheduledArrival);
        Assert.Equal(earlier.Stops[0].ExpectedArrival, response.ExpectedArrival);
        Assert.Equal(platformId, response.Platform!.Id);
        Assert.Equal("1", response.Platform.Number);
        Assert.Equal(earlier.Status, response.Status);
        Assert.Equal(earlier.LastSequenceNumber, response.SequenceNumber);
        Assert.Equal(earlier.LastUpdatedAt, response.LastUpdatedAt);
        Assert.Equal(serviceId, Assert.Single(timetable.RequestedServiceIds));
    }

    [Fact]
    public async Task An_absent_date_uses_today_in_the_railway_time_zone()
    {
        // Arrange
        var stationId = Guid.CreateVersion7();
        var destinationId = Guid.CreateVersion7();
        var platformId = Guid.CreateVersion7();
        var lineId = Guid.CreateVersion7();
        var serviceId = Guid.CreateVersion7();
        var now = new DateTimeOffset(2026, 10, 30, 23, 30, 0, TimeSpan.Zero);
        var railwayToday = new DateOnly(2026, 10, 31);
        var station = TestData.Station(stationId, platformId, lineId);
        var destination = TestData.Station(destinationId, Guid.CreateVersion7(), lineId, "Galle");
        var journey = JourneyCallingAt(
            stationId,
            destinationId,
            platformId,
            lineId,
            serviceId,
            railwayToday,
            TestData.Now,
            TestData.Now);
        var liveJourneys = new StubLiveJourneys(journey);
        var handler = HandlerDriver.CreateQueryHandler<GetStationJourneysQuery, IReadOnlyList<ProviderJourneyResponse>>(
            new StubNetworkCatalog([station, destination], [TestData.Line(lineId)]),
            new StubTimetable(TestData.Service(serviceId, lineId)),
            liveJourneys,
            new StubTimeProvider(now),
            RailwayTimeZone);

        // Act
        var result = await handler.Handle(new GetStationJourneysQuery(stationId, null), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal(railwayToday, Assert.Single(liveJourneys.RequestedDates));
    }

    [Fact]
    public async Task Missing_catalogue_details_are_returned_as_null_without_inventing_values()
    {
        // Arrange
        var stationId = Guid.CreateVersion7();
        var destinationId = Guid.CreateVersion7();
        var unknownPlatformId = Guid.CreateVersion7();
        var lineId = Guid.CreateVersion7();
        var serviceId = Guid.CreateVersion7();
        var serviceDate = DateOnly.FromDateTime(TestData.Now.DateTime);
        var station = TestData.Station(stationId, Guid.CreateVersion7(), lineId);
        var journey = JourneyCallingAt(
            stationId,
            destinationId,
            unknownPlatformId,
            lineId,
            serviceId,
            serviceDate,
            TestData.Now,
            TestData.Now);
        var handler = HandlerDriver.CreateQueryHandler<GetStationJourneysQuery, IReadOnlyList<ProviderJourneyResponse>>(
            new StubNetworkCatalog([station]),
            new StubTimetable(),
            new StubLiveJourneys(journey),
            new StubTimeProvider(TestData.Now),
            RailwayTimeZone);

        // Act
        var result = await handler.Handle(
            new GetStationJourneysQuery(stationId, serviceDate),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        var response = Assert.Single(result.Value);
        Assert.Null(response.Line.Name);
        Assert.Null(response.Direction);
        Assert.Null(response.FinalDestination.Name);
        Assert.Null(response.Platform);
    }

    [Fact]
    public async Task An_unknown_station_returns_not_found_without_reading_live_journeys()
    {
        // Arrange
        var liveJourneys = new StubLiveJourneys();
        var handler = HandlerDriver.CreateQueryHandler<GetStationJourneysQuery, IReadOnlyList<ProviderJourneyResponse>>(
            new StubNetworkCatalog(),
            new StubTimetable(),
            liveJourneys,
            new StubTimeProvider(TestData.Now),
            RailwayTimeZone);

        // Act
        var result = await handler.Handle(
            new GetStationJourneysQuery(Guid.CreateVersion7(), null),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal(0, liveJourneys.GetByDateCount);
    }

    [Fact]
    public async Task A_network_failure_is_propagated_before_live_journeys_are_read()
    {
        // Arrange
        var network = new StubNetworkCatalog
        {
            ExceptionToThrow = new InvalidOperationException("Network catalogue unavailable."),
        };
        var liveJourneys = new StubLiveJourneys();
        var handler = HandlerDriver.CreateQueryHandler<GetStationJourneysQuery, IReadOnlyList<ProviderJourneyResponse>>(
            network,
            new StubTimetable(),
            liveJourneys,
            new StubTimeProvider(TestData.Now),
            RailwayTimeZone);

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new GetStationJourneysQuery(Guid.CreateVersion7(), null),
            CancellationToken.None));

        // Assert
        Assert.Equal("Network catalogue unavailable.", exception.Message);
        Assert.Equal(0, liveJourneys.GetByDateCount);
    }

    private static LiveJourneyView JourneyCallingAt(
        Guid stationId,
        Guid destinationId,
        Guid platformId,
        Guid lineId,
        Guid serviceId,
        DateOnly serviceDate,
        DateTimeOffset scheduledArrival,
        DateTimeOffset expectedArrival) =>
        TestData.Journey(
            Guid.CreateVersion7(),
            serviceId,
            lineId,
            [
                TestData.Stop(
                    1,
                    stationId,
                    scheduledArrival,
                    platformId,
                    expectedArrival: expectedArrival),
                TestData.Stop(2, destinationId, scheduledArrival.AddHours(1)),
            ],
            serviceDate);
}
