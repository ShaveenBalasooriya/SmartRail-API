using SmartRail.LiveOperations.Contracts;

namespace SmartRail.Distribution.Application.Public.Departures;

/// <summary>
/// Chooses the upcoming departures a passenger list shows and builds each item from the one public
/// journey view (design D1). The selection is pure: it reads nothing, writes nothing and never
/// throws on data it cannot resolve, so the rules of the passenger-departures spec can be checked
/// on their own.
/// </summary>
/// <remarks>
/// A calling stop is a departure only when it has a scheduled departure. A journey's final calling
/// stop has none, which is what keeps the terminus out of its own station's departure list.
/// </remarks>
public static class PublicDepartureSelection
{
    /// <summary>How long after a departed stop the journey is still shown there, with its actual departure (passenger-departures spec).</summary>
    public const int DepartedGraceMinutes = 2;

    /// <summary>
    /// How long a cancelled journey, or an early-terminated journey at a station after its last
    /// served stop, stays listed after its scheduled departure (passenger-departures spec).
    /// </summary>
    public const int CancelledOrTerminatedGraceMinutes = 10;

    /// <summary>The window a station's departures cover when the caller names none: two hours (passenger-departures spec).</summary>
    public const int DefaultWindowMinutes = 120;

    /// <summary>The widest window a caller may ask for: six hours (passenger-departures spec).</summary>
    public const int MaximumWindowMinutes = 360;

    /// <summary>
    /// Journeys that call at <paramref name="from"/> and later at <paramref name="to"/>, ordered by
    /// their departure from <paramref name="from"/>. A journey that passes either station without
    /// calling, or that reaches <paramref name="to"/> before <paramref name="from"/>, is left out;
    /// a change of line is never considered (passenger-departures spec).
    /// </summary>
    public static IReadOnlyList<PublicDepartureItem> Between(
        IReadOnlyList<PublicJourneyView> journeys,
        Guid from,
        Guid to,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(journeys);

        var selected = new List<Selection>();

        foreach (var journey in journeys)
        {
            if (CallingStop(journey, from) is not { } origin || !CallsLaterAt(journey, origin, to))
            {
                continue;
            }

            if (Select(journey, origin, now, windowMinutes: null) is { } departure)
            {
                selected.Add(departure);
            }
        }

        return Ordered(selected);
    }

    /// <summary>
    /// Journeys departing <paramref name="stationId"/> inside the next <paramref name="windowMinutes"/>,
    /// ordered by their departure. The interchange station returns the onward departures of every
    /// line that serves it, each with its own line and destination (passenger-departures spec).
    /// </summary>
    public static IReadOnlyList<PublicDepartureItem> FromStation(
        IReadOnlyList<PublicJourneyView> journeys,
        Guid stationId,
        int windowMinutes,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(journeys);

        var selected = new List<Selection>();

        foreach (var journey in journeys)
        {
            if (CallingStop(journey, stationId) is not { } origin)
            {
                continue;
            }

            if (Select(journey, origin, now, windowMinutes) is { } departure)
            {
                selected.Add(departure);
            }
        }

        return Ordered(selected);
    }

    /// <summary>Builds the item for one stop, or returns <see langword="null"/> when it is not upcoming.</summary>
    private static Selection? Select(
        PublicJourneyView journey,
        PublicStopView origin,
        DateTimeOffset now,
        int? windowMinutes)
    {
        // A final calling stop has no departure, so it is never a departure for its station.
        if (origin.ScheduledDeparture is not { } scheduled)
        {
            return null;
        }

        // A finished journey is no longer upcoming anywhere.
        if (journey.Status is JourneyLifecycleStatus.Completed)
        {
            return null;
        }

        var afterLastServed = journey.Status is JourneyLifecycleStatus.Terminated
            && IsAfterLastServed(journey, origin);

        // Cancelled and terminated journeys are placed by their scheduled departure; everything else
        // by the departure the live state currently expects.
        var departsAt = journey.Status is JourneyLifecycleStatus.Cancelled or JourneyLifecycleStatus.Terminated
            ? scheduled
            : origin.ActualDeparture ?? origin.ExpectedDeparture ?? scheduled;

        var graceMinutes = journey.Status is JourneyLifecycleStatus.Cancelled || afterLastServed
            ? CancelledOrTerminatedGraceMinutes
            : DepartedGraceMinutes;

        if (now > departsAt.AddMinutes(graceMinutes))
        {
            return null;
        }

        if (windowMinutes is { } window && departsAt > now.AddMinutes(window))
        {
            return null;
        }

        if (FinalCallingStop(journey) is not { } destination)
        {
            return null;
        }

        return new Selection(
            new PublicDepartureItem(journey, origin, destination.Station, MinutesLate(origin)),
            departsAt);
    }

    /// <summary>The first calling stop at <paramref name="stationId"/>, which carries the departure.</summary>
    private static PublicStopView? CallingStop(PublicJourneyView journey, Guid stationId) =>
        journey.Stops
            .Where(stop => stop.IsCalling && stop.Station.Id == stationId)
            .MinBy(stop => stop.Sequence);

    /// <summary>True when the journey calls at <paramref name="stationId"/> after <paramref name="origin"/>.</summary>
    private static bool CallsLaterAt(PublicJourneyView journey, PublicStopView origin, Guid stationId) =>
        journey.Stops.Any(stop => stop.IsCalling
            && stop.Station.Id == stationId
            && stop.Sequence > origin.Sequence);

    /// <summary>The journey's final calling stop, which is where every departure of it goes.</summary>
    private static PublicStopView? FinalCallingStop(PublicJourneyView journey) =>
        journey.Stops.Where(stop => stop.IsCalling).MaxBy(stop => stop.Sequence);

    /// <summary>
    /// The last calling stop the train served: the live state gives no expected times to any stop
    /// after the one an early termination ends the journey at.
    /// </summary>
    private static bool IsAfterLastServed(PublicJourneyView journey, PublicStopView origin)
    {
        var lastServed = journey.Stops
            .Where(stop => stop.IsCalling
                && (stop.ExpectedArrival is not null
                    || stop.ExpectedDeparture is not null
                    || stop.ActualDeparture is not null))
            .MaxBy(stop => stop.Sequence);

        return lastServed is null || origin.Sequence > lastServed.Sequence;
    }

    /// <summary>
    /// How early or late the stop is, from the departure the live state records or expects against
    /// the timetabled one. A stop with no expected or actual departure reports nothing.
    /// </summary>
    private static int? MinutesLate(PublicStopView origin)
    {
        if (origin.ScheduledDeparture is not { } scheduled
            || (origin.ActualDeparture ?? origin.ExpectedDeparture) is not { } departure)
        {
            return null;
        }

        return (int)Math.Round((departure - scheduled).TotalMinutes, MidpointRounding.AwayFromZero);
    }

    /// <summary>Orders by departure time, then by journey identifier so equal times never swap.</summary>
    private static IReadOnlyList<PublicDepartureItem> Ordered(List<Selection> selected) =>
        [.. selected
            .OrderBy(selection => selection.DepartsAt)
            .ThenBy(selection => selection.Item.Journey.JourneyId)
            .Select(selection => selection.Item)];

    /// <summary>One selected departure with the instant it is ordered by.</summary>
    private sealed record Selection(PublicDepartureItem Item, DateTimeOffset DepartsAt);
}
