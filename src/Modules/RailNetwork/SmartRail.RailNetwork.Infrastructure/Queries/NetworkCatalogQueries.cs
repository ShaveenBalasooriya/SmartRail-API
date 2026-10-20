using Microsoft.EntityFrameworkCore;
using SmartRail.RailNetwork.Contracts;
using SmartRail.RailNetwork.Domain.Entities;
using SmartRail.RailNetwork.Infrastructure.Persistence;
using ServiceDirection = SmartRail.RailNetwork.Contracts.ServiceDirection;

namespace SmartRail.RailNetwork.Infrastructure.Queries;

/// <summary>
/// Reads the physical network from the module's own schema for other modules and the public
/// network endpoints (design D4). Everything is projected and untracked, so no entity leaves the
/// module.
/// </summary>
internal sealed class NetworkCatalogQueries(RailNetworkDbContext database) : INetworkCatalog
{
    public async Task<IReadOnlyList<LineDto>> GetLinesAsync(
        ServiceDirection direction = ServiceDirection.Outbound,
        CancellationToken cancellationToken = default)
    {
        var lines = await database.Lines
            .AsNoTracking()
            .OrderBy(line => line.Code)
            .Select(line => new LineDto(
                line.Id,
                line.Code,
                line.Name,
                line.LineStations
                    .OrderBy(lineStation => lineStation.Sequence)
                    .Select(lineStation => new LineStationDto(
                        lineStation.StationId,
                        lineStation.Station.Code,
                        lineStation.Station.Name,
                        lineStation.Sequence))
                    .ToList()))
            .ToListAsync(cancellationToken);

        // Inbound reads the same stations backwards (data-model.md), so it is never stored.
        return direction == ServiceDirection.Inbound
            ? lines.Select(line => line with { Stations = line.Stations.Reverse().ToList() }).ToList()
            : lines;
    }

    public async Task<IReadOnlyList<StationDto>> GetStationsAsync(CancellationToken cancellationToken = default)
    {
        return await ProjectStations(database.Stations
                .AsNoTracking()
                .OrderBy(station => station.Code))
            .ToListAsync(cancellationToken);
    }

    public async Task<StationDto?> GetStationAsync(Guid stationId, CancellationToken cancellationToken = default)
    {
        return await ProjectStations(database.Stations
                .AsNoTracking()
                .Where(station => station.Id == stationId))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PlatformDto>> GetPlatformsAsync(
        Guid stationId,
        CancellationToken cancellationToken = default)
    {
        return await database.Platforms
            .AsNoTracking()
            .Where(platform => platform.StationId == stationId)
            .OrderBy(platform => platform.Number)
            .Select(platform => new PlatformDto(platform.Id, platform.StationId, platform.Number))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> IsInterchangeAsync(Guid stationId, CancellationToken cancellationToken = default)
    {
        var lineCount = await database.LineStations
            .AsNoTracking()
            .Where(lineStation => lineStation.StationId == stationId)
            .Select(lineStation => lineStation.LineId)
            .Distinct()
            .CountAsync(cancellationToken);

        return lineCount >= 2;
    }

    private IQueryable<StationDto> ProjectStations(IQueryable<Station> stations)
    {
        return stations.Select(station => new StationDto(
            station.Id,
            station.Code,
            station.Name,
            database.LineStations
                .Where(lineStation => lineStation.StationId == station.Id)
                .Select(lineStation => lineStation.LineId)
                .Distinct()
                .Count() >= 2,
            database.Platforms
                .Where(platform => platform.StationId == station.Id)
                .OrderBy(platform => platform.Number)
                .Select(platform => new PlatformDto(platform.Id, platform.StationId, platform.Number))
                .ToList(),
            database.LineStations
                .Where(lineStation => lineStation.StationId == station.Id)
                .OrderBy(lineStation => lineStation.Line.Code)
                .Select(lineStation => new StationLineDto(
                    lineStation.LineId,
                    lineStation.Line.Code,
                    lineStation.Line.Name))
                .ToList()));
    }
}
