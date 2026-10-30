using SmartRail.Distribution.Application.Public.Departures;

namespace SmartRail.Distribution.Application.Public.Display;

/// <summary>
/// One platform's board as the station display shows it (station-display-feed spec): the departures
/// whose current platform is that one, plus the notices of trains that have left it for another
/// platform. Both lists reuse the passenger departure item, so the times, status, platform-change
/// flag and freshness are the same data the passenger app reads (design D1).
/// </summary>
/// <param name="Departures">The upcoming departures currently assigned to the platform, in expected-departure order.</param>
/// <param name="MovedNotices">
/// The trains that left this platform for another one, until their scheduled departure has passed.
/// They are not departures of the platform any more, so they are a separate list rather than items
/// of the board.
/// </param>
public sealed record PublicPlatformFeed(
    IReadOnlyList<PublicDepartureItem> Departures,
    IReadOnlyList<PublicMovedNotice> MovedNotices);

/// <summary>
/// A train that has left one platform for another, as the old platform's display announces it
/// (station-display-feed spec, "Re-platformed train").
/// </summary>
/// <param name="JourneyId">The journey that moved.</param>
/// <param name="Destination">The journey's final calling stop, which is where it goes.</param>
/// <param name="ScheduledDeparture">The timetabled departure from the station, which ends the notice once it has passed.</param>
/// <param name="NewPlatform">The platform the journey is assigned to now.</param>
/// <param name="Message">The full sentence the display shows.</param>
public sealed record PublicMovedNotice(
    Guid JourneyId,
    PublicStationRef Destination,
    DateTimeOffset ScheduledDeparture,
    PublicPlatformRef NewPlatform,
    string Message);
