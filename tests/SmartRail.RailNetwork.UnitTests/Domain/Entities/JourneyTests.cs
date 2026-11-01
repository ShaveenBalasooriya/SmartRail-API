using SmartRail.RailNetwork.Domain.Entities;
using SmartRail.RailNetwork.UnitTests.TestDoubles;

namespace SmartRail.RailNetwork.UnitTests.Domain.Entities;

/// <summary>
/// How a service becomes one dated journey (journey-generation spec). The rules are the entity's
/// own: which stop supplies the scheduled departure, which day it belongs to, and when a service is
/// too incomplete to produce a journey at all.
/// </summary>
public sealed class JourneyTests
{
    [Fact]
    public void Happy_path_creates_a_dated_journey_from_the_first_stops_local_departure()
    {
        // Arrange
        var service = TestData.TwoStopService();
        var clock = TestData.Clock();

        // Act
        var result = Journey.Create(service, TestData.ServiceDate, clock);

        // Assert
        Assert.True(result.IsSuccess);
        var journey = result.Value;
        Assert.NotEqual(Guid.Empty, journey.Id);
        Assert.Equal(service.Id, journey.ServiceId);
        Assert.Equal(TestData.ServiceDate, journey.ServiceDate);

        // 08:00 in the railway's zone (UTC+05:30) is 02:30 UTC on the same day.
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 2, 30, 0, TimeSpan.Zero), journey.ScheduledDepartureAt);
        Assert.Equal(TimeSpan.Zero, journey.ScheduledDepartureAt.Offset);

        // The creation time is the clock's instant, not the departure.
        Assert.Equal(TestData.Now, journey.CreatedAt);
    }

    [Fact]
    public void The_scheduled_departure_is_the_lowest_sequence_stop_whichever_order_the_stops_arrive_in()
    {
        // Arrange: the second stop is added first and departs later, so only the ordering rule can
        // pick the right one.
        var first = TestData.CallingStop(1, arrival: null, departure: new TimeOnly(8, 0));
        var second = TestData.CallingStop(2, arrival: new TimeOnly(8, 15), departure: new TimeOnly(8, 20));
        var service = TestData.ServiceWith(second, first);
        var clock = TestData.Clock();

        // Act
        var result = Journey.Create(service, TestData.ServiceDate, clock);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(
            new DateTimeOffset(2026, 11, 1, 2, 30, 0, TimeSpan.Zero),
            result.Value.ScheduledDepartureAt);
    }

    [Fact]
    public void A_departure_just_after_local_midnight_falls_on_the_previous_utc_day()
    {
        // Arrange
        var service = TestData.ServiceWith(
            TestData.CallingStop(1, arrival: null, departure: new TimeOnly(0, 10)));
        var clock = TestData.Clock();

        // Act
        var result = Journey.Create(service, TestData.ServiceDate, clock);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(
            new DateTimeOffset(2026, 10, 31, 18, 40, 0, TimeSpan.Zero),
            result.Value.ScheduledDepartureAt);
    }

    [Fact]
    public void A_service_with_no_stops_cannot_become_a_journey()
    {
        // Arrange
        var service = TestData.CreateService();

        // Act
        var result = Journey.Create(service, TestData.ServiceDate, TestData.Clock());

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(JourneyErrors.FirstStopDepartureRequired, result.Error);
    }

    [Fact]
    public void A_service_whose_first_stop_has_no_departure_cannot_become_a_journey()
    {
        // Arrange: the first stop only arrives, so there is no departure to schedule.
        var service = TestData.ServiceWith(
            TestData.CallingStop(1, arrival: new TimeOnly(8, 0), departure: null));

        // Act
        var result = Journey.Create(service, TestData.ServiceDate, TestData.Clock());

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(JourneyErrors.FirstStopDepartureRequired, result.Error);
    }

    [Fact]
    public void A_null_service_is_rejected()
    {
        // Arrange

        // Act
        var exception = Assert.Throws<ArgumentNullException>(
            () => Journey.Create(null!, TestData.ServiceDate, TestData.Clock()));

        // Assert
        Assert.Equal("service", exception.ParamName);
    }

    [Fact]
    public void A_null_clock_is_rejected()
    {
        // Arrange
        var service = TestData.TwoStopService();

        // Act
        var exception = Assert.Throws<ArgumentNullException>(
            () => Journey.Create(service, TestData.ServiceDate, clock: null!));

        // Assert
        Assert.Equal("clock", exception.ParamName);
    }
}
