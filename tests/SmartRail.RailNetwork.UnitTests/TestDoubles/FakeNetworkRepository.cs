using SmartRail.RailNetwork.Application.Network;
using SmartRail.RailNetwork.Contracts;

namespace SmartRail.RailNetwork.UnitTests.TestDoubles;

/// <summary>
/// An in-memory network repository. It answers with the data it was given and records the direction
/// each line request asked for, so the handler's direction rule can be checked without a database.
/// </summary>
internal sealed class FakeNetworkRepository : INetworkRepository
{
    private readonly Dictionary<ServiceDirection, IReadOnlyList<LineDto>> _linesByDirection = [];

    /// <summary>The direction of every GetLinesAsync call, in call order.</summary>
    internal List<ServiceDirection> RequestedDirections { get; } = [];

    /// <summary>The stations GetStationsAsync returns.</summary>
    internal IReadOnlyList<StationDto> Stations { get; set; } = [];

    /// <summary>The stations GetStationAsync can find, by identifier.</summary>
    internal Dictionary<Guid, StationDto> StationsById { get; } = [];

    internal FakeNetworkRepository WithLines(ServiceDirection direction, params LineDto[] lines)
    {
        _linesByDirection[direction] = lines;
        return this;
    }

    public Task<IReadOnlyList<LineDto>> GetLinesAsync(
        ServiceDirection direction,
        CancellationToken cancellationToken = default)
    {
        RequestedDirections.Add(direction);

        return Task.FromResult(_linesByDirection.GetValueOrDefault(direction) ?? []);
    }

    public Task<IReadOnlyList<StationDto>> GetStationsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Stations);

    public Task<StationDto?> GetStationAsync(Guid stationId, CancellationToken cancellationToken = default) =>
        Task.FromResult(StationsById.GetValueOrDefault(stationId));
}
