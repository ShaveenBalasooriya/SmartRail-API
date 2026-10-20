using MediatR;
using SmartRail.RailNetwork.Application.Network.GetLines;
using SmartRail.RailNetwork.Application.Network.GetStation;
using SmartRail.RailNetwork.Application.Network.GetStations;
using SmartRail.RailNetwork.Contracts;

namespace SmartRail.RailNetwork.Application.Network;

/// <summary>
/// Implements the module's network contract by sending its own queries, so another module's call and
/// an HTTP request run through the same handler (design D3, architecture §3). The contract cannot
/// fail, so a single-result query unwraps its successful value and an unknown station is null.
/// </summary>
internal sealed class NetworkCatalogService(ISender sender) : INetworkCatalog
{
    public async Task<IReadOnlyList<LineDto>> GetLinesAsync(
        ServiceDirection direction = ServiceDirection.Outbound,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(new GetLinesQuery(direction.ToString()), cancellationToken);

        return result.Value;
    }

    public async Task<IReadOnlyList<StationDto>> GetStationsAsync(CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(new GetStationsQuery(), cancellationToken);

        return result.Value;
    }

    public async Task<StationDto?> GetStationAsync(
        Guid stationId,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(new GetStationQuery(stationId), cancellationToken);

        return result.IsSuccess ? result.Value : null;
    }

    public async Task<IReadOnlyList<PlatformDto>> GetPlatformsAsync(
        Guid stationId,
        CancellationToken cancellationToken = default)
    {
        var station = await GetStationAsync(stationId, cancellationToken);

        return station?.Platforms ?? [];
    }

    public async Task<bool> IsInterchangeAsync(Guid stationId, CancellationToken cancellationToken = default)
    {
        var station = await GetStationAsync(stationId, cancellationToken);

        return station?.IsInterchange ?? false;
    }
}
