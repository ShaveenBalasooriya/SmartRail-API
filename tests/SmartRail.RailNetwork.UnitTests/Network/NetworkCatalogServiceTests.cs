using SmartRail.RailNetwork.Application.Network;
using SmartRail.RailNetwork.Application.Network.GetLines;
using SmartRail.RailNetwork.Application.Network.GetStation;
using SmartRail.RailNetwork.Application.Network.GetStations;
using SmartRail.RailNetwork.Contracts;
using SmartRail.RailNetwork.UnitTests.TestDoubles;
using SmartRail.SharedKernel.Results;

namespace SmartRail.RailNetwork.UnitTests.Network;

/// <summary>
/// The network contract's implementation (design D3, architecture section 3). Each call is exactly
/// one of the module's own queries, and a single-result query that fails becomes <c>null</c> or an
/// empty list, because the contract cannot report an error.
/// </summary>
public sealed class NetworkCatalogServiceTests
{
    [Fact]
    public async Task Happy_path_get_lines_sends_the_requested_direction_and_returns_the_lines()
    {
        // Arrange
        var lines = new[] { TestData.Line(TestData.LineStation(Guid.CreateVersion7(), 1)) };
        var sender = new StubSender { LinesResult = Result.Success<IReadOnlyList<LineDto>>(lines) };
        var catalog = new NetworkCatalogService(sender);

        // Act
        var result = await catalog.GetLinesAsync(ServiceDirection.Inbound);

        // Assert
        Assert.Equal(lines, result);
        var query = Assert.IsType<GetLinesQuery>(Assert.Single(sender.Sent));
        Assert.Equal("Inbound", query.Direction);
    }

    [Fact]
    public async Task The_default_line_direction_is_outbound()
    {
        // Arrange
        var sender = new StubSender();
        var catalog = new NetworkCatalogService(sender);

        // Act
        await catalog.GetLinesAsync();

        // Assert
        var query = Assert.IsType<GetLinesQuery>(Assert.Single(sender.Sent));
        Assert.Equal("Outbound", query.Direction);
    }

    [Fact]
    public async Task Get_stations_returns_every_station()
    {
        // Arrange
        var stations = new[] { TestData.Station(code: "S01"), TestData.Station(code: "S02") };
        var sender = new StubSender { StationsResult = Result.Success<IReadOnlyList<StationDto>>(stations) };
        var catalog = new NetworkCatalogService(sender);

        // Act
        var result = await catalog.GetStationsAsync();

        // Assert
        Assert.Equal(stations, result);
        Assert.IsType<GetStationsQuery>(Assert.Single(sender.Sent));
    }

    [Fact]
    public async Task A_known_station_is_returned()
    {
        // Arrange
        var station = TestData.Station();
        var sender = new StubSender { StationResult = Result.Success(station) };
        var catalog = new NetworkCatalogService(sender);

        // Act
        var result = await catalog.GetStationAsync(station.Id);

        // Assert
        Assert.Same(station, result);
        var query = Assert.IsType<GetStationQuery>(Assert.Single(sender.Sent));
        Assert.Equal(station.Id, query.StationId);
    }

    [Fact]
    public async Task An_unknown_station_is_null()
    {
        // Arrange
        var sender = new StubSender
        {
            StationResult = Result.Failure<StationDto>(Error.NotFound("station.not_found", "No station.")),
        };
        var catalog = new NetworkCatalogService(sender);

        // Act
        var result = await catalog.GetStationAsync(Guid.CreateVersion7());

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task Get_platforms_returns_the_platforms_of_the_station()
    {
        // Arrange
        var station = TestData.Station(platforms: [TestData.Platform(Guid.CreateVersion7(), "1")]);
        var sender = new StubSender { StationResult = Result.Success(station) };
        var catalog = new NetworkCatalogService(sender);

        // Act
        var result = await catalog.GetPlatformsAsync(station.Id);

        // Assert
        Assert.Equal(station.Platforms, result);
    }

    [Fact]
    public async Task Is_interchange_reports_what_the_station_says()
    {
        // Arrange
        var station = TestData.Station(isInterchange: true);
        var sender = new StubSender { StationResult = Result.Success(station) };
        var catalog = new NetworkCatalogService(sender);

        // Act
        var result = await catalog.IsInterchangeAsync(station.Id);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task An_unknown_station_has_no_platforms_and_is_not_an_interchange()
    {
        // Arrange
        var sender = new StubSender
        {
            StationResult = Result.Failure<StationDto>(Error.NotFound("station.not_found", "No station.")),
        };
        var catalog = new NetworkCatalogService(sender);

        // Act
        var platforms = await catalog.GetPlatformsAsync(Guid.CreateVersion7());
        var isInterchange = await catalog.IsInterchangeAsync(Guid.CreateVersion7());

        // Assert
        Assert.Empty(platforms);
        Assert.False(isInterchange);
    }
}
