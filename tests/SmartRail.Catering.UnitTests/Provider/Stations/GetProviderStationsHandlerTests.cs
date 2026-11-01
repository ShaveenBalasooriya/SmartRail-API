using SmartRail.Catering.Application.Provider.Stations;
using SmartRail.Catering.Application.Provider.Stations.GetProviderStations;
using SmartRail.Catering.UnitTests.TestDoubles;

namespace SmartRail.Catering.UnitTests.Provider.Stations;

public sealed class GetProviderStationsHandlerTests
{
    [Fact]
    public async Task Happy_path_returns_every_station_with_its_provider_view()
    {
        // Arrange
        var first = TestData.Station(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var second = TestData.Station(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "Kandy");
        var handler = HandlerDriver.CreateQueryHandler<GetProviderStationsQuery, IReadOnlyList<ProviderStationResponse>>(new StubNetworkCatalog([first, second]));

        // Act
        var result = await handler.Handle(new GetProviderStationsQuery(), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.Equal(first.Id, result.Value[0].Id);
        Assert.Equal(second.Id, result.Value[1].Id);
        Assert.Equal("Kandy", result.Value[1].Name);
        Assert.Single(result.Value[1].Platforms);
        Assert.Single(result.Value[1].Lines);
    }
}
