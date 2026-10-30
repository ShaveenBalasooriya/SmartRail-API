using System.Globalization;
using SmartRail.Distribution.Application.Public.Departures;
using SmartRail.RailNetwork.Contracts;

namespace SmartRail.Distribution.Application.Public.Display;

/// <summary>
/// Chooses what one platform's display shows from the station's passenger departures
/// (station-display-feed spec, design D1). The selection is pure: it reads nothing, writes nothing
/// and never throws on data it cannot resolve, so the placement and notice rules can be checked on
/// their own.
/// </summary>
/// <remarks>
/// The "still listed" rules are the station departure list's own, reused through
/// <see cref="PublicDepartureSelection.FromStation"/>, so a train disappears from the platform at the
/// same moment it disappears from the passenger list. A journey is placed by the platform it is
/// assigned to now; the notice of a move stands on the platform the timetable assigned, which is the
/// one the display showed before the change.
/// </remarks>
public static class PublicPlatformSelection
{
    /// <summary>How many departures a platform shows when the caller names no count (station-display-feed spec).</summary>
    public const int DefaultCount = 8;

    /// <summary>The most departures a caller may ask for (station-display-feed spec).</summary>
    public const int MaximumCount = 20;

    /// <summary>The clock format the notice uses: 24-hour, minutes, in the railway's time zone.</summary>
    private const string TimeFormat = "HH:mm";

    /// <summary>
    /// The feed of <paramref name="platformId"/> at <paramref name="stationId"/>: the journeys
    /// currently assigned to it in expected-departure order, limited to <paramref name="count"/>, and
    /// the notices of journeys that left it for another platform.
    /// </summary>
    public static PublicPlatformFeed Feed(
        IReadOnlyList<PublicJourneyView> journeys,
        Guid stationId,
        Guid platformId,
        int count,
        DateTimeOffset now,
        NetworkLookup lookup,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(journeys);
        ArgumentNullException.ThrowIfNull(lookup);
        ArgumentNullException.ThrowIfNull(timeZone);

        var departures = PublicDepartureSelection.FromStation(
            journeys,
            stationId,
            PublicDepartureSelection.DefaultWindowMinutes,
            now);

        var onPlatform = departures
            .Where(departure => departure.Origin.Platform?.Id == platformId)
            .Take(count)
            .ToArray();

        var movedNotices = departures
            .Where(departure => HasMoved(departure, platformId, now, lookup))
            .Select(departure => Notice(departure, timeZone))
            .OfType<PublicMovedNotice>()
            .Take(count)
            .ToArray();

        return new PublicPlatformFeed(onPlatform, movedNotices);
    }

    /// <summary>
    /// True when the departure left <paramref name="platformId"/> for another platform and its
    /// scheduled departure has not passed yet. A train still on the platform, or with no recorded
    /// platform change, has not moved.
    /// </summary>
    private static bool HasMoved(
        PublicDepartureItem departure,
        Guid platformId,
        DateTimeOffset now,
        NetworkLookup lookup)
    {
        var origin = departure.Origin;

        if (origin.PlatformChangedAt is null || origin.Platform?.Id == platformId)
        {
            return false;
        }

        if (origin.ScheduledDeparture is not { } scheduled || now > scheduled)
        {
            return false;
        }

        return TimetabledPlatform(departure.Journey, origin.Station.Id, lookup) == platformId;
    }

    /// <summary>The platform the timetable assigns the journey's first calling stop at the station.</summary>
    private static Guid? TimetabledPlatform(
        PublicJourneyView journey,
        Guid stationId,
        NetworkLookup lookup) =>
        lookup.Services.GetValueOrDefault(journey.ServiceId)?.Stops
            .Where(stop => stop.IsCalling && stop.StationId == stationId)
            .MinBy(stop => stop.Sequence)
            ?.PlatformId;

    /// <summary>
    /// The notice for one moved departure, or <see langword="null"/> when a value the sentence needs
    /// is missing, so the display never shows a sentence with a hole in it.
    /// </summary>
    private static PublicMovedNotice? Notice(PublicDepartureItem departure, TimeZoneInfo timeZone)
    {
        var origin = departure.Origin;

        if (origin.ScheduledDeparture is not { } scheduled
            || origin.Platform is not { } platform
            || platform.Number is not { } number
            || departure.Destination.Name is not { } destination)
        {
            return null;
        }

        var time = TimeZoneInfo
            .ConvertTime(scheduled, timeZone)
            .ToString(TimeFormat, CultureInfo.InvariantCulture);

        return new PublicMovedNotice(
            departure.Journey.JourneyId,
            departure.Destination,
            scheduled,
            platform,
            $"The {time} service to {destination} has moved to platform {number}.");
    }
}
