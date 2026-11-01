using SmartRail.RailNetwork.Application.Network.GetLines;
using SmartRail.RailNetwork.Contracts;
using SmartRail.RailNetwork.UnitTests.TestDoubles;

namespace SmartRail.RailNetwork.UnitTests.Network.GetLines;

/// <summary>
/// The lines query handler: it translates the wire direction to the contract enum and returns what
/// the repository read (design D3). The validator normally stops an unknown value, but the handler
/// still has a defined answer for one, so it is checked here rather than assumed.
/// </summary>
public sealed class GetLinesQueryHandlerTests
{
    [Fact]
    public async Task Happy_path_returns_the_lines_of_the_requested_direction()
    {
        // Arrange
        var line = TestData.Line(
            TestData.LineStation(Guid.CreateVersion7(), 1),
            TestData.LineStation(Guid.CreateVersion7(), 2));
        var network = new FakeNetworkRepository().WithLines(ServiceDirection.Inbound, line);
        var handler = new GetLinesQueryHandler(network);

        // Act
        var result = await handler.Handle(new GetLinesQuery("inbound"), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Same(line, Assert.Single(result.Value));
        Assert.Equal([ServiceDirection.Inbound], network.RequestedDirections);
    }

    [Fact]
    public async Task A_blank_direction_is_read_as_outbound()
    {
        // Arrange
        var network = new FakeNetworkRepository();
        var handler = new GetLinesQueryHandler(network);

        // Act
        var result = await handler.Handle(new GetLinesQuery("   "), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal([ServiceDirection.Outbound], network.RequestedDirections);
    }

    [Fact]
    public async Task An_unknown_direction_is_read_as_outbound()
    {
        // Arrange
        var network = new FakeNetworkRepository();
        var handler = new GetLinesQueryHandler(network);

        // Act
        var result = await handler.Handle(new GetLinesQuery("sideways"), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal([ServiceDirection.Outbound], network.RequestedDirections);
    }

    [Fact]
    public async Task A_null_request_is_rejected()
    {
        // Arrange
        var handler = new GetLinesQueryHandler(new FakeNetworkRepository());

        // Act
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => handler.Handle(null!, CancellationToken.None));

        // Assert
        Assert.Equal("request", exception.ParamName);
    }
}
