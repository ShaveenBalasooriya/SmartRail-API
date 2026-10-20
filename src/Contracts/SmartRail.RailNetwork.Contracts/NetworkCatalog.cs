namespace SmartRail.RailNetwork.Contracts;

/// <summary>
/// Read access to the physical network: lines with their ordered stations, stations with their
/// platforms and lines, and the interchange (architecture §3). Other modules use this instead of
/// reading the <c>network</c> schema.
/// </summary>
public interface INetworkCatalog
{
    /// <summary>
    /// Every line, each with its distinct stations in sequence order; inbound returns the same
    /// stations in reverse order.
    /// </summary>
    Task<IReadOnlyList<LineDto>> GetLinesAsync(
        ServiceDirection direction = ServiceDirection.Outbound,
        CancellationToken cancellationToken = default);

    /// <summary>Every station with its platforms and the lines it belongs to.</summary>
    Task<IReadOnlyList<StationDto>> GetStationsAsync(CancellationToken cancellationToken = default);

    /// <summary>One station, or <see langword="null"/> when the ID is unknown.</summary>
    Task<StationDto?> GetStationAsync(Guid stationId, CancellationToken cancellationToken = default);

    /// <summary>The platforms of one station; empty when the station has none.</summary>
    Task<IReadOnlyList<PlatformDto>> GetPlatformsAsync(Guid stationId, CancellationToken cancellationToken = default);

    /// <summary>True when the station appears on two or more lines.</summary>
    Task<bool> IsInterchangeAsync(Guid stationId, CancellationToken cancellationToken = default);
}

/// <summary>A line and its stations in the requested direction's sequence order.</summary>
public sealed record LineDto(
    Guid Id,
    string Code,
    string Name,
    IReadOnlyList<LineStationDto> Stations);

/// <summary>One station's place in a line's order.</summary>
public sealed record LineStationDto(
    Guid StationId,
    string Code,
    string Name,
    int Sequence);

/// <summary>A station with its platforms and the lines it belongs to.</summary>
public sealed record StationDto(
    Guid Id,
    string Code,
    string Name,
    bool IsInterchange,
    IReadOnlyList<PlatformDto> Platforms,
    IReadOnlyList<StationLineDto> Lines);

/// <summary>A platform of a station. The number is unique within its station.</summary>
public sealed record PlatformDto(
    Guid Id,
    Guid StationId,
    string Number);

/// <summary>One of the lines a station belongs to.</summary>
public sealed record StationLineDto(
    Guid Id,
    string Code,
    string Name);
