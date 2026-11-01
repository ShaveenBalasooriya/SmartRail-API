using SmartRail.Catering.Application.Provider.Stations;
using SmartRail.Catering.Application.Provider.Stations.GetProviderStation;
using SmartRail.Catering.UnitTests.TestDoubles;
using SmartRail.SharedKernel.Results;

namespace SmartRail.Catering.UnitTests.Provider.Stations;

public sealed class GetProviderStationHandlerTests
{
    [Fact]
    public async Task Happy_path_maps_the_station_platforms_and_lines_for_the_provider()
    {
        // Arrange
        var stationId = Guid.CreateVersion7();
        var platformId = Guid.CreateVersion7();
        var lineId = Guid.CreateVersion7();
        var station = TestData.Station(stationId, platformId, lineId);
        var handler = HandlerDriver.CreateQueryHandler<GetProviderStationQuery, ProviderStationResponse>(new StubNetworkCatalog([station]));

        // Act
        var result = await handler.Handle(new GetProviderStationQuery(stationId), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(stationId, result.Value.Id);
        Assert.Equal("CMB", result.Value.Code);
        Assert.Equal("Colombo Fort", result.Value.Name);
        Assert.True(result.Value.IsInterchange);
        var platform = Assert.Single(result.Value.Platforms);
        Assert.Equal(platformId, platform.Id);
        Assert.Equal("1", platform.Number);
        var line = Assert.Single(result.Value.Lines);
        Assert.Equal(lineId, line.Id);
        Assert.Equal("Main Line", line.Name);
    }

    [Fact]
    public async Task An_unknown_station_returns_not_found()
    {
        // Arrange
        var handler = HandlerDriver.CreateQueryHandler<GetProviderStationQuery, ProviderStationResponse>(new StubNetworkCatalog());

        // Act
        var result = await handler.Handle(new GetProviderStationQuery(Guid.CreateVersion7()), CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }
}
