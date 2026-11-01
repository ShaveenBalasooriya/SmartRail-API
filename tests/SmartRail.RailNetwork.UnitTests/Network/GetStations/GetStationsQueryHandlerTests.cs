using SmartRail.RailNetwork.Application.Network.GetStations;
using SmartRail.RailNetwork.UnitTests.TestDoubles;

namespace SmartRail.RailNetwork.UnitTests.Network.GetStations;

/// <summary>
/// The stations query handler (design D3): every station with its platforms and lines, or an empty
/// list when the network has none.
/// </summary>
public sealed class GetStationsQueryHandlerTests
{
    [Fact]
    public async Task Happy_path_returns_every_station()
    {
        // Arrange
        var first = TestData.Station(code: "S01", name: "Central");
        var second = TestData.Station(code: "S02", name: "Harbour", isInterchange: true);
        var network = new FakeNetworkRepository { Stations = [first, second] };
        var handler = new GetStationsQueryHandler(network);

        // Act
        var result = await handler.Handle(new GetStationsQuery(), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal([first, second], result.Value);
    }

    [Fact]
    public async Task A_network_with_no_station_returns_an_empty_list()
    {
        // Arrange
        var handler = new GetStationsQueryHandler(new FakeNetworkRepository());

        // Act
        var result = await handler.Handle(new GetStationsQuery(), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }
}
