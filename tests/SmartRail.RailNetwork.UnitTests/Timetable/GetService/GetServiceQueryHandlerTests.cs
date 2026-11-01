using SmartRail.RailNetwork.Application.Timetable.GetService;
using SmartRail.RailNetwork.UnitTests.TestDoubles;

namespace SmartRail.RailNetwork.UnitTests.Timetable.GetService;

/// <summary>
/// One timetable service by identifier (design D3): a known identifier returns the service with its
/// stops, an unknown one is the not-found failure the API turns into a 404.
/// </summary>
public sealed class GetServiceQueryHandlerTests
{
    [Fact]
    public async Task Happy_path_returns_the_service_with_its_stops()
    {
        // Arrange
        var service = TestData.ScheduledService(
            stops:
            [
                TestData.ScheduledStop(1, departure: TestData.FirstDeparture),
                TestData.ScheduledStop(2, arrival: TestData.SecondArrival),
            ]);
        var timetable = new FakeTimetableRepository();
        timetable.ServicesById[service.Id] = service;
        var handler = new GetServiceQueryHandler(timetable);

        // Act
        var result = await handler.Handle(new GetServiceQuery(service.Id), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Same(service, result.Value);
        Assert.Equal(2, result.Value.Stops.Count);
    }

    [Fact]
    public async Task An_unknown_identifier_is_not_found()
    {
        // Arrange
        var handler = new GetServiceQueryHandler(new FakeTimetableRepository());

        // Act
        var result = await handler.Handle(new GetServiceQuery(Guid.CreateVersion7()), CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("service.not_found", result.Error.Code);
        Assert.Equal(SmartRail.SharedKernel.Results.ErrorType.NotFound, result.Error.Type);
    }

    [Fact]
    public async Task A_null_request_is_rejected()
    {
        // Arrange
        var handler = new GetServiceQueryHandler(new FakeTimetableRepository());

        // Act
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => handler.Handle(null!, CancellationToken.None));

        // Assert
        Assert.Equal("request", exception.ParamName);
    }
}
