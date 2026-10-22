using SmartRail.RailNetwork.Contracts;

namespace SmartRail.RailNetwork.Application.Network;

/// <summary>Reads the physical network from the module's own schema.</summary>
public interface INetworkRepository
{
    /// <summary>
    /// Every line with its stations in sequence order; inbound returns the same stations reversed.
    /// </summary>
    Task<IReadOnlyList<LineDto>> GetLinesAsync(
        ServiceDirection direction,
        CancellationToken cancellationToken = default);

    /// <summary>Every station with its platforms and the lines it belongs to.</summary>
    Task<IReadOnlyList<StationDto>> GetStationsAsync(CancellationToken cancellationToken = default);

    /// <summary>One station, or <see langword="null"/> when the ID is unknown.</summary>
    Task<StationDto?> GetStationAsync(Guid stationId, CancellationToken cancellationToken = default);
}
