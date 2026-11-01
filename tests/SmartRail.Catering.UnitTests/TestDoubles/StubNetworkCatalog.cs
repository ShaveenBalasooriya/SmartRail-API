using SmartRail.RailNetwork.Contracts;

namespace SmartRail.Catering.UnitTests.TestDoubles;

/// <summary>A network catalogue backed by the stations and lines supplied by each test.</summary>
internal sealed class StubNetworkCatalog(
    IReadOnlyList<StationDto>? stations = null,
    IReadOnlyList<LineDto>? lines = null) : INetworkCatalog
{
    private readonly IReadOnlyList<StationDto> _stations = stations ?? [];
    private readonly IReadOnlyList<LineDto> _lines = lines ?? [];

    public int GetStationsCount { get; private set; }

    public Exception? ExceptionToThrow { get; set; }

    public Task<IReadOnlyList<LineDto>> GetLinesAsync(
        ServiceDirection direction = ServiceDirection.Outbound,
        CancellationToken cancellationToken = default)
    {
        ThrowIfRequested();
        return Task.FromResult(_lines);
    }

    public Task<IReadOnlyList<StationDto>> GetStationsAsync(CancellationToken cancellationToken = default)
    {
        GetStationsCount++;
        ThrowIfRequested();
        return Task.FromResult(_stations);
    }

    public Task<StationDto?> GetStationAsync(Guid stationId, CancellationToken cancellationToken = default)
    {
        ThrowIfRequested();
        return Task.FromResult(_stations.FirstOrDefault(station => station.Id == stationId));
    }

    public Task<IReadOnlyList<PlatformDto>> GetPlatformsAsync(
        Guid stationId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfRequested();
        return Task.FromResult<IReadOnlyList<PlatformDto>>(
            [.. _stations.FirstOrDefault(station => station.Id == stationId)?.Platforms ?? []]);
    }

    public Task<bool> IsInterchangeAsync(Guid stationId, CancellationToken cancellationToken = default)
    {
        ThrowIfRequested();
        return Task.FromResult(_stations.FirstOrDefault(station => station.Id == stationId)?.IsInterchange ?? false);
    }

    private void ThrowIfRequested()
    {
        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }
    }
}
