namespace SmartRail.Distribution.Application.Public.Departures;

/// <summary>
/// One upcoming departure as a passenger reads it: the journey, the stop of the station the caller
/// asked about, where the journey ends and how late it is at that stop (passenger-departures spec).
/// It reuses the one public journey view, so a departure carries the same times, platform,
/// provenance, correction and freshness as every other public channel (design D1).
/// </summary>
/// <param name="Journey">The journey the departure belongs to, with every stop and its guidance.</param>
/// <param name="Origin">The journey's stop at the station the caller asked about.</param>
/// <param name="Destination">The journey's final calling stop, which is where the departure goes.</param>
/// <param name="MinutesLate">
/// Minutes early (negative) or late (positive) at <paramref name="Origin"/>; <see langword="null"/>
/// when the stop carries no expected or actual departure, as a cancelled or an unserved terminated
/// journey does.
/// </param>
public sealed record PublicDepartureItem(
    PublicJourneyView Journey,
    PublicStopView Origin,
    PublicStationRef Destination,
    int? MinutesLate);
