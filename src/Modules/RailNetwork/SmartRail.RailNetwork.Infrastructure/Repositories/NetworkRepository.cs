using Microsoft.EntityFrameworkCore;
using SmartRail.RailNetwork.Application.Network;
using SmartRail.RailNetwork.Contracts;
using SmartRail.RailNetwork.Domain.Entities;
using SmartRail.RailNetwork.Infrastructure.Persistence;
using ServiceDirection = SmartRail.RailNetwork.Contracts.ServiceDirection;

namespace SmartRail.RailNetwork.Infrastructure.Repositories;

/// <summary>
/// Reads the physical network from the module's own schema. Entities are loaded untracked and
/// projected in memory, so no entity leaves the module.
/// </summary>
internal sealed class NetworkRepository(RailNetworkDbContext database) : INetworkRepository
{
    public async Task<IReadOnlyList<LineDto>> GetLinesAsync(
        ServiceDirection direction,
        CancellationToken cancellationToken = default)
    {
        var lines = await database.Lines
            .AsNoTracking()
            .Include(line => line.LineStations)
            .ThenInclude(lineStation => lineStation.Station)
            .OrderBy(line => line.Code)
            .ToListAsync(cancellationToken);

        var mapped = lines
            .Select(line => new LineDto(
                line.Id,
                line.Code.Value,
                line.Name,
                line.LineStations
                    .OrderBy(lineStation => lineStation.Sequence)
                    .Select(lineStation => new LineStationDto(
                        lineStation.StationId,
                        lineStation.Station.Code.Value,
                        lineStation.Station.Name,
                        lineStation.Sequence))
                    .ToList()))
            .ToList();

        // Inbound reads the same stations backwards (data-model.md), so it is never stored.
        return direction == ServiceDirection.Inbound
            ? mapped.Select(line => line with { Stations = line.Stations.Reverse().ToList() }).ToList()
            : mapped;
    }

    public async Task<IReadOnlyList<StationDto>> GetStationsAsync(CancellationToken cancellationToken = default)
    {
        var stations = await database.Stations
            .AsNoTracking()
            .OrderBy(station => station.Code)
            .ToListAsync(cancellationToken);

        return await ProjectStationsAsync(stations, cancellationToken);
    }

    public async Task<StationDto?> GetStationAsync(
        Guid stationId,
        CancellationToken cancellationToken = default)
    {
        var station = await database.Stations
            .AsNoTracking()
            .Where(station => station.Id == stationId)
            .SingleOrDefaultAsync(cancellationToken);

        if (station is null)
        {
            return null;
        }

        var projected = await ProjectStationsAsync([station], cancellationToken);

        return projected.SingleOrDefault();
    }

    private async Task<IReadOnlyList<StationDto>> ProjectStationsAsync(
        List<Station> stations,
        CancellationToken cancellationToken)
    {
        if (stations.Count == 0)
        {
            return [];
        }

        var stationIds = stations.Select(station => station.Id).ToList();

        var platforms = await database.Platforms
            .AsNoTracking()
            .Where(platform => stationIds.Contains(platform.StationId))
            .ToListAsync(cancellationToken);

        var memberships = await database.LineStations
            .AsNoTracking()
            .Include(lineStation => lineStation.Line)
            .Where(lineStation => stationIds.Contains(lineStation.StationId))
            .ToListAsync(cancellationToken);

        return stations
            .Select(station =>
            {
                var stationPlatforms = platforms
                    .Where(platform => platform.StationId == station.Id)
                    .OrderBy(platform => platform.Number.Value)
                    .Select(platform => new PlatformDto(platform.Id, platform.StationId, platform.Number.Value))
                    .ToList();

                var stationLines = memberships
                    .Where(lineStation => lineStation.StationId == station.Id)
                    .OrderBy(lineStation => lineStation.Line.Code.Value)
                    .Select(lineStation => new StationLineDto(
                        lineStation.LineId,
                        lineStation.Line.Code.Value,
                        lineStation.Line.Name))
                    .ToList();

                // A station is an interchange when it belongs to two or more lines (data-model.md).
                var isInterchange = memberships
                    .Where(lineStation => lineStation.StationId == station.Id)
                    .Select(lineStation => lineStation.LineId)
                    .Distinct()
                    .Count() >= 2;

                return new StationDto(
                    station.Id,
                    station.Code.Value,
                    station.Name,
                    isInterchange,
                    stationPlatforms,
                    stationLines);
            })
            .ToList();
    }
}
