using SmartRail.RailNetwork.Application.Network.GetStation;
using SmartRail.RailNetwork.UnitTests.TestDoubles;

namespace SmartRail.RailNetwork.UnitTests.Network.GetStation;

/// <summary>
/// One station by identifier (design D3): a known identifier returns the station, an unknown one is
/// the not-found failure the API turns into a 404.
/// </summary>
public sealed class GetStationQueryHandlerTests
{
    [Fact]
    public async Task Happy_path_returns_the_station_with_its_platforms_and_lines()
    {
        // Arrange
        var line = TestData.Line();
        var station = TestData.Station(
            isInterchange: true,
            platforms: [TestData.Platform(Guid.CreateVersion7(), "2")],
            lines: [TestData.StationLine(line)]);
        var network = new FakeNetworkRepository();
        network.StationsById[station.Id] = station;
        var handler = new GetStationQueryHandler(network);

        // Act
        var result = await handler.Handle(new GetStationQuery(station.Id), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Same(station, result.Value);
        Assert.True(result.Value.IsInterchange);
        Assert.Single(result.Value.Platforms);
        Assert.Single(result.Value.Lines);
    }

    [Fact]
    public async Task An_unknown_identifier_is_not_found()
    {
        // Arrange
        var handler = new GetStationQueryHandler(new FakeNetworkRepository());

        // Act
        var result = await handler.Handle(new GetStationQuery(Guid.CreateVersion7()), CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("station.not_found", result.Error.Code);
        Assert.Equal(SmartRail.SharedKernel.Results.ErrorType.NotFound, result.Error.Type);
    }

    [Fact]
    public async Task A_null_request_is_rejected()
    {
        // Arrange
        var handler = new GetStationQueryHandler(new FakeNetworkRepository());

        // Act
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => handler.Handle(null!, CancellationToken.None));

        // Assert
        Assert.Equal("request", exception.ParamName);
    }
}
