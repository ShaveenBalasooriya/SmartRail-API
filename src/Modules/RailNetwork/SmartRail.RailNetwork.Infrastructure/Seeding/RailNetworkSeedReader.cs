using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using SmartRail.RailNetwork.Domain.Entities;
using SmartRail.RailNetwork.Domain.ValueObjects;

namespace SmartRail.RailNetwork.Infrastructure.Seeding;

/// <summary>
/// Reads and validates <c>seed/network/network.json</c> and <c>seed/network/timetable.json</c> before
/// anything is written (design D4, D6). A file that is missing, malformed or breaks a rule stops
/// start-up with the file named and, where a record is at fault, its 1-based position.
/// </summary>
/// <remarks>
/// The dataset rules are checked here rather than by the domain because the domain cannot see a whole
/// service: it does not know the line's station order, which stop is first, or the times of the stops
/// before it.
/// </remarks>
internal static class RailNetworkSeedReader
{
    /// <summary>The network seed file, relative to the seed root.</summary>
    public const string NetworkSeedFile = "network/network.json";

    /// <summary>The timetable seed file, relative to the seed root.</summary>
    public const string TimetableSeedFile = "network/timetable.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        // An unexpected property is a mistake in the file, not something to ignore silently.
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>Reads both files and returns the validated dataset.</summary>
    public static RailNetworkSeed Read(string seedRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(seedRoot);

        var networkPath = Path.Combine(seedRoot, NetworkSeedFile);
        var timetablePath = Path.Combine(seedRoot, TimetableSeedFile);

        var network = ReadJson<NetworkFileRecord>(networkPath);
        var timetable = ReadJson<TimetableFileRecord>(timetablePath);

        var stationRecords = network.Stations;
        if (stationRecords is not { Count: > 0 })
        {
            throw new InvalidOperationException($"Seed file '{networkPath}' contains no stations.");
        }

        var lineRecords = network.Lines;
        if (lineRecords is not { Count: > 0 })
        {
            throw new InvalidOperationException($"Seed file '{networkPath}' contains no lines.");
        }

        var serviceRecords = timetable.Services;
        if (serviceRecords is not { Count: > 0 })
        {
            throw new InvalidOperationException($"Seed file '{timetablePath}' contains no services.");
        }

        // One set for both files, so an identifier can never be shared by two records (design D3).
        var ids = new HashSet<Guid>();
        var platformsById = new Dictionary<Guid, PlatformSeed>();

        var (stations, stationsByCode) = ReadStations(networkPath, stationRecords, ids, platformsById);
        var lines = ReadLines(networkPath, lineRecords, stationsByCode, ids);
        var services = ReadServices(
            timetablePath,
            serviceRecords,
            lines.ToDictionary(line => line.Id),
            stations.ToDictionary(station => station.Id),
            platformsById,
            ids);

        return new RailNetworkSeed(stations, lines, services);
    }

    private static (List<StationSeed> Stations, Dictionary<string, StationSeed> ByCode) ReadStations(
        string path,
        List<StationRecord> records,
        HashSet<Guid> ids,
        Dictionary<Guid, PlatformSeed> platformsById)
    {
        var stations = new List<StationSeed>(records.Count);
        var byCode = new Dictionary<string, StationSeed>(StringComparer.Ordinal);

        for (var index = 0; index < records.Count; index++)
        {
            var record = records[index];
            var position = index + 1;
            var label = Label(record.Code);

            var code = StationCode.Create(record.Code);
            if (code.IsFailure)
            {
                throw InvalidRecord(path, position, label, code.Error.Description);
            }

            var station = Station.Create(record.Id, code.Value, record.Name);
            if (station.IsFailure)
            {
                throw InvalidRecord(path, position, label, station.Error.Description);
            }

            if (!ids.Add(station.Value.Id))
            {
                throw InvalidRecord(
                    path,
                    position,
                    label,
                    $"the identifier '{station.Value.Id}' is used more than once.");
            }

            if (byCode.ContainsKey(station.Value.Code.Value))
            {
                throw InvalidRecord(
                    path,
                    position,
                    label,
                    $"station code '{station.Value.Code.Value}' is used more than once.");
            }

            var platformRecords = record.Platforms;
            if (platformRecords is not { Count: > 0 })
            {
                throw InvalidRecord(path, position, label, "a station needs at least one platform.");
            }

            var platforms = new List<PlatformSeed>(platformRecords.Count);
            var numbers = new HashSet<string>(StringComparer.Ordinal);

            for (var platformIndex = 0; platformIndex < platformRecords.Count; platformIndex++)
            {
                var platformRecord = platformRecords[platformIndex];
                var platformLabel = $"{station.Value.Code.Value} platform {Label(platformRecord.Number)}";

                var number = PlatformNumber.Create(platformRecord.Number);
                if (number.IsFailure)
                {
                    throw InvalidRecord(path, position, platformLabel, number.Error.Description);
                }

                var platform = Platform.Create(platformRecord.Id, station.Value.Id, number.Value);
                if (platform.IsFailure)
                {
                    throw InvalidRecord(path, position, platformLabel, platform.Error.Description);
                }

                if (!ids.Add(platform.Value.Id))
                {
                    throw InvalidRecord(
                        path,
                        position,
                        platformLabel,
                        $"the identifier '{platform.Value.Id}' is used more than once.");
                }

                if (!numbers.Add(platform.Value.Number.Value))
                {
                    throw InvalidRecord(
                        path,
                        position,
                        platformLabel,
                        $"platform number '{platform.Value.Number.Value}' is used more than once at this station.");
                }

                var platformSeed = new PlatformSeed(platform.Value.Id, station.Value.Id, platform.Value.Number);
                platforms.Add(platformSeed);
                platformsById.Add(platformSeed.Id, platformSeed);
            }

            var stationSeed = new StationSeed(station.Value.Id, station.Value.Code, station.Value.Name, platforms);
            stations.Add(stationSeed);
            byCode.Add(stationSeed.Code.Value, stationSeed);
        }

        return (stations, byCode);
    }

    private static List<LineSeed> ReadLines(
        string path,
        List<LineRecord> records,
        Dictionary<string, StationSeed> stationsByCode,
        HashSet<Guid> ids)
    {
        var lines = new List<LineSeed>(records.Count);
        var codes = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < records.Count; index++)
        {
            var record = records[index];
            var position = index + 1;
            var label = Label(record.Code);

            var code = LineCode.Create(record.Code);
            if (code.IsFailure)
            {
                throw InvalidRecord(path, position, label, code.Error.Description);
            }

            var color = LineColor.Create(record.Color);
            if (color.IsFailure)
            {
                throw InvalidRecord(path, position, label, color.Error.Description);
            }

            var line = Line.Create(record.Id, code.Value, color.Value, record.Name);
            if (line.IsFailure)
            {
                throw InvalidRecord(path, position, label, line.Error.Description);
            }

            if (!ids.Add(line.Value.Id))
            {
                throw InvalidRecord(
                    path,
                    position,
                    label,
                    $"the identifier '{line.Value.Id}' is used more than once.");
            }

            if (!codes.Add(line.Value.Code.Value))
            {
                throw InvalidRecord(
                    path,
                    position,
                    label,
                    $"line code '{line.Value.Code.Value}' is used more than once.");
            }

            var stationCodes = record.Stations;
            if (stationCodes is not { Count: > 0 })
            {
                throw InvalidRecord(path, position, label, "a line needs at least one station.");
            }

            var stationIds = new List<Guid>(stationCodes.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (var stationIndex = 0; stationIndex < stationCodes.Count; stationIndex++)
            {
                var stationCode = stationCodes[stationIndex];
                if (string.IsNullOrWhiteSpace(stationCode))
                {
                    throw InvalidRecord(path, position, label, "a line's station reference is required.");
                }

                if (!seen.Add(stationCode))
                {
                    throw InvalidRecord(
                        path,
                        position,
                        label,
                        $"station '{stationCode}' appears more than once in the line's order.");
                }

                if (!stationsByCode.TryGetValue(stationCode, out var station))
                {
                    throw new InvalidOperationException(
                        $"Seed file '{path}' contains an unknown station reference '{stationCode}' "
                        + $"in line record {position} ('{line.Value.Code.Value}'), position {stationIndex + 1}.");
                }

                stationIds.Add(station.Id);
            }

            lines.Add(new LineSeed(
                line.Value.Id,
                line.Value.Code,
                line.Value.Color,
                line.Value.Name,
                stationIds));
        }

        return lines;
    }

    private static List<ServiceSeed> ReadServices(
        string path,
        List<ServiceRecord> records,
        Dictionary<Guid, LineSeed> linesById,
        Dictionary<Guid, StationSeed> stationsById,
        Dictionary<Guid, PlatformSeed> platformsById,
        HashSet<Guid> ids)
    {
        var services = new List<ServiceSeed>(records.Count);
        var naturalKeys = new HashSet<(Guid LineId, ServiceDirection Direction, string Name)>();

        for (var index = 0; index < records.Count; index++)
        {
            var record = records[index];
            var position = index + 1;
            var label = Label(record.Name);

            if (!linesById.TryGetValue(record.LineId, out var line))
            {
                throw new InvalidOperationException(
                    $"Seed file '{path}' contains an unknown line reference '{record.LineId}' "
                    + $"in service record {position} ('{label}').");
            }

            var direction = record.Direction switch
            {
                "outbound" => ServiceDirection.Outbound,
                "inbound" => ServiceDirection.Inbound,
                _ => throw InvalidRecord(path, position, label, ServiceErrors.DirectionInvalid.Description),
            };

            var service = Service.Create(record.Id, line.Id, direction, record.Name, record.GuardStaffId);
            if (service.IsFailure)
            {
                throw InvalidRecord(path, position, label, service.Error.Description);
            }

            if (!ids.Add(service.Value.Id))
            {
                throw InvalidRecord(
                    path,
                    position,
                    label,
                    $"the identifier '{service.Value.Id}' is used more than once.");
            }

            if (!naturalKeys.Add((service.Value.LineId, service.Value.Direction, service.Value.Name)))
            {
                throw InvalidRecord(
                    path,
                    position,
                    label,
                    $"a service named '{service.Value.Name}' already runs {record.Direction} on line '{line.Code.Value}'.");
            }

            var stopRecords = record.Stops;
            if (stopRecords is not { Count: > 0 })
            {
                throw InvalidRecord(path, position, label, "a service needs at least one stop.");
            }

            var stops = new List<ServiceStopSeed>(stopRecords.Count);
            var sequences = new HashSet<int>();

            for (var stopIndex = 0; stopIndex < stopRecords.Count; stopIndex++)
            {
                var stopRecord = stopRecords[stopIndex];
                var stopPosition = stopIndex + 1;

                if (!sequences.Add(stopRecord.Sequence))
                {
                    throw InvalidRecord(
                        path,
                        position,
                        label,
                        $"stop sequence {stopRecord.Sequence} appears more than once.");
                }

                if (!stationsById.ContainsKey(stopRecord.StationId))
                {
                    throw new InvalidOperationException(
                        $"Seed file '{path}' contains an unknown station reference '{stopRecord.StationId}' "
                        + $"in service record {position} ('{label}'), stop {stopPosition}.");
                }

                if (stopRecord.PlatformId is { } platformId)
                {
                    if (!platformsById.TryGetValue(platformId, out var platform))
                    {
                        throw new InvalidOperationException(
                            $"Seed file '{path}' contains an unknown platform reference '{platformId}' "
                            + $"in service record {position} ('{label}'), stop {stopPosition}.");
                    }

                    if (platform.StationId != stopRecord.StationId)
                    {
                        throw new InvalidOperationException(
                            $"Seed file '{path}' contains a platform '{platformId}' that belongs to another station "
                            + $"in service record {position} ('{label}'), stop {stopPosition}.");
                    }
                }

                var arrival = ParseTime(stopRecord.Arrival, path, position, label, stopPosition, "arrival");
                var departure = ParseTime(stopRecord.Departure, path, position, label, stopPosition, "departure");

                var stop = ServiceStop.Create(
                    service.Value.Id,
                    stopRecord.StationId,
                    stopRecord.Sequence,
                    stopRecord.IsCalling,
                    arrival,
                    departure,
                    stopRecord.PlatformId);

                if (stop.IsFailure)
                {
                    throw InvalidRecord(path, position, label, stop.Error.Description);
                }

                stops.Add(new ServiceStopSeed(
                    stop.Value.Sequence,
                    stop.Value.StationId,
                    stop.Value.PlatformId,
                    stop.Value.IsCalling,
                    stop.Value.ScheduledArrival,
                    stop.Value.ScheduledDeparture));
            }

            ValidateServiceShape(path, position, label, service.Value, line, stationsById, stops);

            services.Add(new ServiceSeed(
                service.Value.Id,
                service.Value.LineId,
                service.Value.Direction,
                service.Value.Name,
                service.Value.GuardStaffId,
                stops));
        }

        return services;
    }

    private static void ValidateServiceShape(
        string path,
        int position,
        string label,
        Service service,
        LineSeed line,
        Dictionary<Guid, StationSeed> stationsById,
        List<ServiceStopSeed> stops)
    {
        if (stops.Count != line.StationIds.Count)
        {
            throw InvalidRecord(
                path,
                position,
                label,
                $"a service on line '{line.Code.Value}' needs {line.StationIds.Count} stops, one per station; it has {stops.Count}.");
        }

        var orderedStationIds = service.Direction == ServiceDirection.Outbound
            ? line.StationIds
            : line.StationIds.Reverse().ToList();

        for (var index = 0; index < stops.Count; index++)
        {
            var stop = stops[index];

            if (stop.Sequence != index + 1)
            {
                throw InvalidRecord(
                    path,
                    position,
                    label,
                    $"stop sequences must run 1..{stops.Count} without gaps; stop {index + 1} has sequence {stop.Sequence}.");
            }

            if (stop.StationId != orderedStationIds[index])
            {
                throw InvalidRecord(
                    path,
                    position,
                    label,
                    $"stop {index + 1} is station '{stationsById[stop.StationId].Code.Value}' "
                    + $"but the line's {service.Direction} order puts '{stationsById[orderedStationIds[index]].Code.Value}' there.");
            }
        }

        var last = stops.Count - 1;

        if (stops[0].ScheduledArrival is not null)
        {
            throw InvalidRecord(path, position, label, "the first stop cannot have an arrival.");
        }

        if (stops[0].ScheduledDeparture is null)
        {
            throw InvalidRecord(path, position, label, "the first stop needs a departure.");
        }

        if (stops[last].ScheduledDeparture is not null)
        {
            throw InvalidRecord(path, position, label, "the last stop cannot have a departure.");
        }

        if (stops[last].ScheduledArrival is null)
        {
            throw InvalidRecord(path, position, label, "the last stop needs an arrival.");
        }

        for (var index = 1; index < last; index++)
        {
            var stop = stops[index];
            if (stop.IsCalling && (stop.ScheduledArrival is null || stop.ScheduledDeparture is null))
            {
                throw InvalidRecord(
                    path,
                    position,
                    label,
                    $"stop {index + 1} is a calling stop and needs both an arrival and a departure.");
            }
        }

        TimeOnly? previous = null;

        for (var index = 0; index < stops.Count; index++)
        {
            var stop = stops[index];

            if (stop.ScheduledArrival is { } arrival)
            {
                if (previous is { } earlier && arrival < earlier)
                {
                    throw InvalidRecord(
                        path,
                        position,
                        label,
                        $"stop {index + 1} arrives at {Format(arrival)} before the previous departure {Format(earlier)}.");
                }

                previous = arrival;
            }

            if (stop.ScheduledDeparture is { } departure)
            {
                if (previous is { } earlier && departure < earlier)
                {
                    throw InvalidRecord(
                        path,
                        position,
                        label,
                        $"stop {index + 1} departs at {Format(departure)} before the previous time {Format(earlier)}.");
                }

                previous = departure;
            }
        }

        if (stops[0].ScheduledDeparture is { } firstDeparture
            && stops[last].ScheduledArrival is { } lastArrival
            && firstDeparture >= lastArrival)
        {
            throw InvalidRecord(
                path,
                position,
                label,
                $"the first departure {Format(firstDeparture)} must be earlier than the last arrival {Format(lastArrival)}.");
        }
    }

    private static TimeOnly? ParseTime(
        string? value,
        string path,
        int recordPosition,
        string label,
        int stopPosition,
        string field)
    {
        if (value is null)
        {
            return null;
        }

        if (!TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            throw new InvalidOperationException(
                $"Seed file '{path}' record {recordPosition} ('{label}') stop {stopPosition} is invalid: "
                + $"'{value}' is not a {field} time in HH:mm format.");
        }

        return time;
    }

    private static T ReadJson<T>(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Seed file '{path}' was not found.");
        }

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException($"Seed file '{path}' could not be read.", exception);
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions)
                ?? throw new InvalidOperationException($"Seed file '{path}' is not valid seed data: it is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"Seed file '{path}' is not valid seed data: {exception.Message}",
                exception);
        }
    }

    private static string Format(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    private static string Label(string? value) => string.IsNullOrWhiteSpace(value) ? "(unnamed)" : value.Trim();

    private static InvalidOperationException InvalidRecord(string path, int position, string label, string reason) =>
        new($"Seed file '{path}' record {position} ('{label}') is invalid: {reason}");

    private sealed record NetworkFileRecord(List<StationRecord>? Stations, List<LineRecord>? Lines);

    private sealed record StationRecord(Guid Id, string? Code, string Name, List<PlatformRecord>? Platforms);

    private sealed record PlatformRecord(Guid Id, string? Number);

    private sealed record LineRecord(Guid Id, string? Code, string? Color, string Name, List<string?>? Stations);

    private sealed record TimetableFileRecord(List<ServiceRecord>? Services);

    private sealed record ServiceRecord(
        Guid Id,
        Guid LineId,
        string? Direction,
        string Name,
        Guid GuardStaffId,
        List<StopRecord>? Stops);

    private sealed record StopRecord(
        int Sequence,
        Guid StationId,
        Guid? PlatformId,
        bool IsCalling,
        string? Arrival,
        string? Departure);
}
