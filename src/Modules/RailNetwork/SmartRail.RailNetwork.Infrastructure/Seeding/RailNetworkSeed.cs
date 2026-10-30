using SmartRail.RailNetwork.Domain.Entities;
using SmartRail.RailNetwork.Domain.ValueObjects;

namespace SmartRail.RailNetwork.Infrastructure.Seeding;

/// <summary>
/// The network and timetable read from <c>seed/network/</c> and already validated as one dataset
/// (design D4, D6). Every reference in it is resolved: a service stop names the station and platform
/// records it uses, and the lines hold their stations in outbound order.
/// </summary>
internal sealed class RailNetworkSeed
{
    /// <summary>Builds the model and its lookup dictionaries from records that passed validation.</summary>
    public RailNetworkSeed(
        IReadOnlyList<StationSeed> stations,
        IReadOnlyList<LineSeed> lines,
        IReadOnlyList<ServiceSeed> services)
    {
        Stations = stations;
        Lines = lines;
        Services = services;
        StationById = stations.ToDictionary(station => station.Id);
        LineById = lines.ToDictionary(line => line.Id);
        PlatformById = stations.SelectMany(station => station.Platforms).ToDictionary(platform => platform.Id);
    }

    /// <summary>The stations in file order, each with its platforms.</summary>
    public IReadOnlyList<StationSeed> Stations { get; }

    /// <summary>The lines in file order, each with its stations in outbound order.</summary>
    public IReadOnlyList<LineSeed> Lines { get; }

    /// <summary>The services in file order, each with its stops in running order.</summary>
    public IReadOnlyList<ServiceSeed> Services { get; }

    /// <summary>Every station by the identifier the file gives it.</summary>
    public IReadOnlyDictionary<Guid, StationSeed> StationById { get; }

    /// <summary>Every line by the identifier the file gives it.</summary>
    public IReadOnlyDictionary<Guid, LineSeed> LineById { get; }

    /// <summary>Every platform by the identifier the file gives it.</summary>
    public IReadOnlyDictionary<Guid, PlatformSeed> PlatformById { get; }
}

/// <summary>A station from <c>network.json</c> with its platforms.</summary>
internal sealed record StationSeed(Guid Id, StationCode Code, string Name, IReadOnlyList<PlatformSeed> Platforms);

/// <summary>A platform from <c>network.json</c>, owned by one station.</summary>
internal sealed record PlatformSeed(Guid Id, Guid StationId, PlatformNumber Number);

/// <summary>A line from <c>network.json</c> with its stations in outbound order.</summary>
internal sealed record LineSeed(
    Guid Id,
    LineCode Code,
    LineColor Color,
    string Name,
    IReadOnlyList<Guid> StationIds);

/// <summary>A service from <c>timetable.json</c> with its stops in running order.</summary>
internal sealed record ServiceSeed(
    Guid Id,
    Guid LineId,
    ServiceDirection Direction,
    string Name,
    Guid GuardStaffId,
    IReadOnlyList<ServiceStopSeed> Stops);

/// <summary>
/// One stop of a seeded service. The station and platform are the identifiers the files give them,
/// and the times are local times of day; a stop that does not call has no platform and no times.
/// </summary>
internal sealed record ServiceStopSeed(
    int Sequence,
    Guid StationId,
    Guid? PlatformId,
    bool IsCalling,
    TimeOnly? ScheduledArrival,
    TimeOnly? ScheduledDeparture);
