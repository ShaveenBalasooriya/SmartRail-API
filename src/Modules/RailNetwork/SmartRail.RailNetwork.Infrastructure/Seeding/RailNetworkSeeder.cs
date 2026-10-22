using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SmartRail.RailNetwork.Domain.Entities;
using SmartRail.RailNetwork.Infrastructure.Persistence;
using SmartRail.SharedKernel.Application.Abstractions;
using SmartRail.SharedKernel.Results;

namespace SmartRail.RailNetwork.Infrastructure.Seeding;

/// <summary>
/// Seeds the network and the timetable from <c>seed/network/</c> (design D2). It is idempotent by
/// natural key — a station by its code, a platform by its station and number, a line by its code and
/// a service by its line, direction and name — so running it on every start inserts what is missing
/// and changes nothing that exists (design D5, seed-data spec).
/// </summary>
/// <remarks>
/// Both files are read and validated before the first insert, and everything is written by one
/// <c>SaveChanges</c>, so a record at fault cannot leave a partial dataset committed. Stations,
/// platforms and lines go in before the services that reference them; references are resolved
/// through the files' natural keys, so a stored row with a different identifier still receives the
/// timetable that names its code (design D3).
/// </remarks>
internal sealed class RailNetworkSeeder(
    RailNetworkDbContext database,
    IConfiguration configuration,
    ILogger<RailNetworkSeeder> logger) : IModuleSeeder
{
    public string Schema => RailNetworkDbContext.Schema;

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var seed = RailNetworkSeedReader.Read(ResolveSeedRoot(configuration));

        var stationIdsByCode = (await database.Stations
                .Select(station => new { station.Id, station.Code })
                .ToListAsync(cancellationToken))
            .ToDictionary(station => station.Code.Value, station => station.Id, StringComparer.Ordinal);

        var lineIdsByCode = (await database.Lines
                .Select(line => new { line.Id, line.Code })
                .ToListAsync(cancellationToken))
            .ToDictionary(line => line.Code.Value, line => line.Id, StringComparer.Ordinal);

        var platformIdsByKey = (await database.Platforms
                .Select(platform => new { platform.Id, platform.StationId, platform.Number })
                .ToListAsync(cancellationToken))
            .ToDictionary(platform => (platform.StationId, platform.Number.Value), platform => platform.Id);

        var serviceKeys = (await database.Services
                .Select(service => new { service.LineId, service.Direction, service.Name })
                .ToListAsync(cancellationToken))
            .Select(service => (service.LineId, service.Direction, service.Name))
            .ToHashSet();

        var stationIds = seed.Stations.ToDictionary(
            station => station.Id,
            station => stationIdsByCode.TryGetValue(station.Code.Value, out var storedId) ? storedId : station.Id);

        var lineIds = seed.Lines.ToDictionary(
            line => line.Id,
            line => lineIdsByCode.TryGetValue(line.Code.Value, out var storedId) ? storedId : line.Id);

        var platformIds = seed.Stations
            .SelectMany(station => station.Platforms)
            .ToDictionary(
                platform => platform.Id,
                platform =>
                {
                    var stationId = stationIds[platform.StationId];

                    return platformIdsByKey.TryGetValue((stationId, platform.Number.Value), out var storedId)
                        ? storedId
                        : platform.Id;
                });

        var newStations = seed.Stations
            .Where(station => !stationIdsByCode.ContainsKey(station.Code.Value))
            .Select(station => Require(
                Station.Create(station.Id, station.Code, station.Name),
                $"station '{station.Code.Value}'"))
            .ToList();

        // A platform is judged on its own: one missing from a station that already exists is added.
        var newPlatforms = seed.Stations
            .SelectMany(station => station.Platforms.Select(platform => (Platform: platform, StationId: stationIds[station.Id])))
            .Where(candidate => !platformIdsByKey.ContainsKey((candidate.StationId, candidate.Platform.Number.Value)))
            .Select(candidate => Require(
                Platform.Create(candidate.Platform.Id, candidate.StationId, candidate.Platform.Number),
                $"platform '{candidate.Platform.Number.Value}'"))
            .ToList();

        var newLines = seed.Lines
            .Where(line => !lineIdsByCode.ContainsKey(line.Code.Value))
            .Select(line => CreateLine(line, stationIds))
            .ToList();

        var newServices = new List<Service>(seed.Services.Count);
        var newStops = 0;

        foreach (var service in seed.Services)
        {
            var lineId = lineIds[service.LineId];

            if (serviceKeys.Contains((lineId, service.Direction, service.Name)))
            {
                continue;
            }

            var created = Require(
                Service.Create(service.Id, lineId, service.Direction, service.Name, service.GuardStaffId),
                $"service '{service.Name}'");

            foreach (var stop in service.Stops)
            {
                created.Stops.Add(Require(
                    ServiceStop.Create(
                        created.Id,
                        stationIds[stop.StationId],
                        stop.Sequence,
                        stop.IsCalling,
                        stop.ScheduledArrival,
                        stop.ScheduledDeparture,
                        stop.PlatformId is { } platformId ? platformIds[platformId] : null),
                    $"stop {stop.Sequence} of service '{service.Name}'"));
            }

            newStops += service.Stops.Count;
            newServices.Add(created);
        }

        database.Stations.AddRange(newStations);
        database.Platforms.AddRange(newPlatforms);
        database.Lines.AddRange(newLines);
        database.Services.AddRange(newServices);

        // One transaction, so the network and the timetable it carries commit together (design D2).
        await database.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Seeded schema {Schema}: {StationCount} station(s), {PlatformCount} platform(s), "
            + "{LineCount} line(s), {ServiceCount} service(s) and {StopCount} stop(s) added",
            Schema,
            newStations.Count,
            newPlatforms.Count,
            newLines.Count,
            newServices.Count,
            newStops);
    }

    private static Line CreateLine(LineSeed line, Dictionary<Guid, Guid> stationIds)
    {
        var created = Require(Line.Create(line.Id, line.Code, line.Name), $"line '{line.Code.Value}'");

        for (var index = 0; index < line.StationIds.Count; index++)
        {
            created.LineStations.Add(Require(
                LineStation.Create(created.Id, stationIds[line.StationIds[index]], index + 1),
                $"station {index + 1} of line '{line.Code.Value}'"));
        }

        return created;
    }

    /// <summary>
    /// The reader has already checked every rule the factories enforce, so a failure here means the
    /// two disagree: it is a bug, and it must stop start-up rather than drop a record (design D6).
    /// </summary>
    private static T Require<T>(Result<T> result, string record)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"Seeding {record} failed: {result.Error.Description}");
        }

        return result.Value;
    }

    /// <summary>
    /// The seed root is <c>Seed:Path</c> when configured, resolved against the application directory
    /// when relative, and otherwise the <c>seed</c> folder copied next to the binaries (design D8).
    /// </summary>
    private static string ResolveSeedRoot(IConfiguration configuration)
    {
        var configured = configuration["Seed:Path"];

        if (string.IsNullOrWhiteSpace(configured))
        {
            return Path.Combine(AppContext.BaseDirectory, "seed");
        }

        return Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(AppContext.BaseDirectory, configured);
    }
}
