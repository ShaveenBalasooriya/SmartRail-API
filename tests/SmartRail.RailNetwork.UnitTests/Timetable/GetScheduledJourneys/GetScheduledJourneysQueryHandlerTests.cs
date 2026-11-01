using SmartRail.RailNetwork.Application.Timetable.GetScheduledJourneys;
using SmartRail.RailNetwork.UnitTests.TestDoubles;

namespace SmartRail.RailNetwork.UnitTests.Timetable.GetScheduledJourneys;

/// <summary>
/// The scheduled journeys query handler (journey-generation spec): the requested day's journeys, or
/// today's when the request leaves the date out.
/// </summary>
public sealed class GetScheduledJourneysQueryHandlerTests
{
    [Fact]
    public async Task Happy_path_returns_the_journeys_of_the_requested_day()
    {
        // Arrange
        var journey = TestData.ScheduledJourney();
        var timetable = new FakeTimetableRepository();
        timetable.JourneysByDate[TestData.ServiceDate] = [journey];
        var handler = new GetScheduledJourneysQueryHandler(timetable, TestData.Clock());

        // Act
        var result = await handler.Handle(
            new GetScheduledJourneysQuery(TestData.ServiceDate),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Same(journey, Assert.Single(result.Value));
        Assert.Equal([TestData.ServiceDate], timetable.RequestedDates);
    }

    [Fact]
    public async Task A_missing_date_means_today_in_the_railways_time_zone()
    {
        // Arrange
        var journey = TestData.ScheduledJourney();
        var timetable = new FakeTimetableRepository();
        timetable.JourneysByDate[TestData.ServiceDate] = [journey];
        var handler = new GetScheduledJourneysQueryHandler(timetable, TestData.Clock());

        // Act
        var result = await handler.Handle(new GetScheduledJourneysQuery(Date: null), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Same(journey, Assert.Single(result.Value));
        Assert.Equal([TestData.ServiceDate], timetable.RequestedDates);
    }

    [Fact]
    public async Task A_null_request_is_rejected()
    {
        // Arrange
        var handler = new GetScheduledJourneysQueryHandler(new FakeTimetableRepository(), TestData.Clock());

        // Act
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => handler.Handle(null!, CancellationToken.None));

        // Assert
        Assert.Equal("request", exception.ParamName);
    }
}
