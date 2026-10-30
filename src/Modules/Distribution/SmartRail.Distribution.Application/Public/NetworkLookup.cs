using SmartRail.RailNetwork.Contracts;

namespace SmartRail.Distribution.Application.Public;

/// <summary>
/// The names the public view shows for the identifiers the live state holds. The module keeps no
/// tables, so every name comes from the Rail Network catalogue and the timetable (design D1).
/// </summary>
/// <param name="Stations">Every station by identifier; a station's lines are not part of the public view.</param>
/// <param name="Platforms">Every platform of every station by identifier.</param>
/// <param name="Lines">Every line by identifier, with its code, colour, name and station order.</param>
/// <param name="Services">The services the caller asked for, by identifier, with their names and directions.</param>
public sealed record NetworkLookup(
    IReadOnlyDictionary<Guid, StationDto> Stations,
    IReadOnlyDictionary<Guid, PlatformDto> Platforms,
    IReadOnlyDictionary<Guid, LineDto> Lines,
    IReadOnlyDictionary<Guid, ServiceDto> Services);
