using SmartRail.RailNetwork.Domain.Entities;

namespace SmartRail.RailNetwork.UnitTests.Domain.Entities;

/// <summary>
/// What one stop of a service may look like (timetable spec). The factory only enforces what a
/// single stop can know: a calling stop needs a platform and a time, a non-calling stop carries
/// nothing, and a stop after the first needs an arrival. Whether a stop is first or last on the
/// service is the service's own concern, so it is not tested here.
/// </summary>
public sealed class ServiceStopTests
{
    [Fact]
    public void Happy_path_adds_a_first_calling_stop_with_a_departure_and_a_platform()
    {
        // Arrange
        var serviceId = Guid.CreateVersion7();
        var stationId = Guid.CreateVersion7();
        var platformId = Guid.CreateVersion7();
        var departure = new TimeOnly(8, 0);

        // Act
        var result = ServiceStop.Create(
            serviceId,
            stationId,
            sequence: 1,
            isCalling: true,
            scheduledArrival: null,
            scheduledDeparture: departure,
            platformId);

        // Assert
        Assert.True(result.IsSuccess);
        var stop = result.Value;
        Assert.NotEqual(Guid.Empty, stop.Id);
        Assert.Equal(serviceId, stop.ServiceId);
        Assert.Equal(stationId, stop.StationId);
        Assert.Equal(1, stop.Sequence);
        Assert.True(stop.IsCalling);
        Assert.Null(stop.ScheduledArrival);
        Assert.Equal(departure, stop.ScheduledDeparture);
        Assert.Equal(platformId, stop.PlatformId);
    }

    [Fact]
    public void A_later_calling_stop_is_accepted_with_an_arrival_and_a_departure()
    {
        // Arrange
        var arrival = new TimeOnly(8, 30);
        var departure = new TimeOnly(8, 32);

        // Act
        var result = ServiceStop.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            sequence: 2,
            isCalling: true,
            arrival,
            departure,
            Guid.CreateVersion7());

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Sequence);
        Assert.Equal(arrival, result.Value.ScheduledArrival);
        Assert.Equal(departure, result.Value.ScheduledDeparture);
    }

    [Fact]
    public void A_calling_stop_at_the_first_sequence_needs_no_arrival()
    {
        // Arrange: the first stop has nothing to arrive from.

        // Act
        var result = ServiceStop.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            sequence: 1,
            isCalling: true,
            scheduledArrival: null,
            scheduledDeparture: new TimeOnly(8, 0),
            Guid.CreateVersion7());

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.ScheduledArrival);
    }

    [Fact]
    public void A_non_calling_stop_carries_no_times_and_no_platform()
    {
        // Arrange

        // Act
        var result = ServiceStop.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            sequence: 2,
            isCalling: false,
            scheduledArrival: null,
            scheduledDeparture: null,
            platformId: null);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsCalling);
        Assert.Null(result.Value.ScheduledArrival);
        Assert.Null(result.Value.ScheduledDeparture);
        Assert.Null(result.Value.PlatformId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void A_sequence_below_one_is_rejected(int sequence)
    {
        // Arrange

        // Act
        var result = ServiceStop.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            sequence,
            isCalling: true,
            scheduledArrival: null,
            scheduledDeparture: new TimeOnly(8, 0),
            Guid.CreateVersion7());

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ServiceStopErrors.SequenceInvalid, result.Error);
    }

    [Fact]
    public void A_stop_without_a_service_is_rejected()
    {
        // Arrange

        // Act
        var result = ServiceStop.Create(
            Guid.Empty,
            Guid.CreateVersion7(),
            sequence: 1,
            isCalling: true,
            scheduledArrival: null,
            scheduledDeparture: new TimeOnly(8, 0),
            Guid.CreateVersion7());

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ServiceStopErrors.ServiceRequired, result.Error);
    }

    [Fact]
    public void A_stop_without_a_station_is_rejected()
    {
        // Arrange

        // Act
        var result = ServiceStop.Create(
            Guid.CreateVersion7(),
            Guid.Empty,
            sequence: 1,
            isCalling: true,
            scheduledArrival: null,
            scheduledDeparture: new TimeOnly(8, 0),
            Guid.CreateVersion7());

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ServiceStopErrors.StationRequired, result.Error);
    }

    [Fact]
    public void A_non_calling_stop_with_an_arrival_is_rejected()
    {
        // Arrange

        // Act
        var result = ServiceStop.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            sequence: 2,
            isCalling: false,
            scheduledArrival: new TimeOnly(8, 30),
            scheduledDeparture: null,
            platformId: null);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ServiceStopErrors.NonCallingStopHasDetails, result.Error);
    }

    [Fact]
    public void A_non_calling_stop_with_a_departure_is_rejected()
    {
        // Arrange

        // Act
        var result = ServiceStop.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            sequence: 2,
            isCalling: false,
            scheduledArrival: null,
            scheduledDeparture: new TimeOnly(8, 30),
            platformId: null);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ServiceStopErrors.NonCallingStopHasDetails, result.Error);
    }

    [Fact]
    public void A_non_calling_stop_with_a_platform_is_rejected()
    {
        // Arrange

        // Act
        var result = ServiceStop.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            sequence: 2,
            isCalling: false,
            scheduledArrival: null,
            scheduledDeparture: null,
            Guid.CreateVersion7());

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ServiceStopErrors.NonCallingStopHasDetails, result.Error);
    }

    [Fact]
    public void A_calling_stop_without_a_platform_is_rejected()
    {
        // Arrange: a calling stop must say where the train stops.

        // Act
        var result = ServiceStop.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            sequence: 1,
            isCalling: true,
            scheduledArrival: null,
            scheduledDeparture: new TimeOnly(8, 0),
            platformId: null);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ServiceStopErrors.PlatformRequired, result.Error);
    }

    [Fact]
    public void A_calling_stop_with_neither_a_time_nor_a_platform_detail_is_rejected_for_its_times()
    {
        // Arrange: the platform is present, so the missing times are the first thing wrong.

        // Act
        var result = ServiceStop.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            sequence: 1,
            isCalling: true,
            scheduledArrival: null,
            scheduledDeparture: null,
            Guid.CreateVersion7());

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ServiceStopErrors.TimesRequired, result.Error);
    }

    [Fact]
    public void A_later_calling_stop_with_a_departure_but_no_arrival_is_rejected()
    {
        // Arrange

        // Act
        var result = ServiceStop.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            sequence: 2,
            isCalling: true,
            scheduledArrival: null,
            scheduledDeparture: new TimeOnly(8, 30),
            Guid.CreateVersion7());

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ServiceStopErrors.ArrivalRequired, result.Error);
    }
}
