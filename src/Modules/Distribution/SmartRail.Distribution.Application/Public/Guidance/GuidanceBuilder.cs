using System.Globalization;
using SmartRail.LiveOperations.Contracts;

namespace SmartRail.Distribution.Application.Public.Guidance;

/// <summary>
/// Writes the plain-language guidance a disrupted journey carries, from fixed templates in code
/// (design D1, disruption-guidance spec). The builder is pure: it reads nothing and writes nothing,
/// and it fills each template only with values the journey's own live state and the catalogue hold,
/// so a message never carries free-form or AI-generated text (M-11, W-16).
/// </summary>
/// <remarks>
/// A journey can carry several disruptions at once but has one guidance object, so the most serious
/// one is reported: a cancellation before an early termination, then a platform change, then a delay
/// (design D-11). Guidance is derived only from the current live state, so a corrected disruption
/// leaves no guidance behind (disruption-guidance spec, corrections replace guidance).
/// </remarks>
public static class GuidanceBuilder
{
    /// <summary>A delay produces guidance from this many minutes late; a smaller one is shown as minutes late and nothing more (disruption-guidance spec).</summary>
    public const int DelayThresholdMinutes = 5;

    /// <summary>From this many minutes late the guidance also names the next service (disruption-guidance spec).</summary>
    public const int NextServiceThresholdMinutes = 15;

    /// <summary>The clock format every message uses: 24-hour, minutes, in the railway's time zone.</summary>
    private const string TimeFormat = "HH:mm";

    private const string NoLaterServiceSentence = "No later service is scheduled today.";

    /// <summary>
    /// True when guidance for <paramref name="journey"/> names a later service, so the caller has to
    /// read the day's other journeys first (design D6).
    /// </summary>
    public static bool NeedsNextService(LiveJourneyView journey)
    {
        ArgumentNullException.ThrowIfNull(journey);

        return journey.Status is JourneyLifecycleStatus.Cancelled or JourneyLifecycleStatus.Terminated
            || journey.DelayMinutes >= NextServiceThresholdMinutes;
    }

    /// <summary>
    /// The guidance <paramref name="journey"/> carries, or <see langword="null"/> when it is
    /// undisrupted. <paramref name="sameDayJourneys"/> is the service date's journeys, which the next
    /// service is chosen from; the journey itself may be among them.
    /// </summary>
    /// <param name="journey">The journey's live state.</param>
    /// <param name="sameDayJourneys">Every journey on the same railway calendar day, in any order.</param>
    /// <param name="lookup">The names and platforms the message shows.</param>
    /// <param name="timeZone">The railway's time zone, which every time in the message is written in.</param>
    public static PublicGuidance? Build(
        LiveJourneyView journey,
        IReadOnlyList<LiveJourneyView> sameDayJourneys,
        NetworkLookup lookup,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(journey);
        ArgumentNullException.ThrowIfNull(sameDayJourneys);
        ArgumentNullException.ThrowIfNull(lookup);
        ArgumentNullException.ThrowIfNull(timeZone);

        var departure = ScheduledDeparture(FirstCallingStop(journey));
        var destination = NameOf(LastCallingStop(journey)?.StationId, lookup);

        // Every template names the departure and the destination. Without them a message could not
        // be a complete, truthful sentence, so the journey carries none rather than an invented one.
        if (departure is null || destination is null)
        {
            return null;
        }

        var train = Train(Time(departure.Value, timeZone), lookup.Lines.GetValueOrDefault(journey.LineId)?.Name, destination);

        if (journey.Status is JourneyLifecycleStatus.Cancelled)
        {
            return WithNextService(
                PublicGuidanceType.Cancellation,
                $"The {train} is cancelled. Please take the next service.",
                journey,
                sameDayJourneys,
                lookup,
                timeZone);
        }

        if (journey.Status is JourneyLifecycleStatus.Terminated)
        {
            // The last station the train served is the last calling stop it still has expected
            // times for; the live state leaves every stop after it without expected times.
            var lastServed = NameOf(LastServedStop(journey)?.StationId, lookup);

            return lastServed is null
                ? null
                : WithNextService(
                    PublicGuidanceType.Termination,
                    $"The {train} will not continue beyond {lastServed}. "
                        + $"It will not call at the stations after {lastServed}. "
                        + "Please take the next service.",
                    journey,
                    sameDayJourneys,
                    lookup,
                    timeZone);
        }

        var changedStop = FirstChangedStop(journey);
        var changedPlatform = PlatformNumber(changedStop?.PlatformId, lookup);
        var changedStation = NameOf(changedStop?.StationId, lookup);

        if (changedPlatform is not null && changedStation is not null)
        {
            return new PublicGuidance(
                PublicGuidanceType.PlatformChange,
                $"The {train} now departs from platform {changedPlatform} at {changedStation}.",
                NextService: null,
                NoLaterService: false);
        }

        if (journey.DelayMinutes is not { } minutes || minutes < DelayThresholdMinutes)
        {
            return null;
        }

        var delayMessage = $"The {train} is running {minutes} minutes late.";

        return minutes >= NextServiceThresholdMinutes
            ? WithNextService(
                PublicGuidanceType.Delay,
                delayMessage,
                journey,
                sameDayJourneys,
                lookup,
                timeZone)
            : new PublicGuidance(
                PublicGuidanceType.Delay,
                delayMessage,
                NextService: null,
                NoLaterService: false);
    }

    /// <summary>The guidance sentence for <paramref name="type"/> followed by the next service, or by the sentence that none runs later today.</summary>
    private static PublicGuidance WithNextService(
        PublicGuidanceType type,
        string message,
        LiveJourneyView journey,
        IReadOnlyList<LiveJourneyView> sameDayJourneys,
        NetworkLookup lookup,
        TimeZoneInfo timeZone)
    {
        var next = SelectNextService(journey, sameDayJourneys);

        if (next is null)
        {
            return new PublicGuidance(
                type,
                $"{message} {NoLaterServiceSentence}",
                NextService: null,
                NoLaterService: true);
        }

        var origin = FirstCallingStop(next)!;
        var scheduled = ScheduledDeparture(origin)!.Value;
        var expected = origin.ExpectedDeparture ?? origin.ExpectedArrival ?? scheduled;
        var destination = NameOf(LastCallingStop(next)?.StationId, lookup)!;
        var platformNumber = PlatformNumber(origin.PlatformId, lookup);

        var sentence = platformNumber is null
            ? $"The next service is the {Time(scheduled, timeZone)} to {destination}, "
                + $"expected at {Time(expected, timeZone)}."
            : $"The next service is the {Time(scheduled, timeZone)} to {destination}, "
                + $"expected at {Time(expected, timeZone)} from platform {platformNumber}.";

        var nextService = new PublicNextService(
            next.JourneyId,
            lookup.Services.GetValueOrDefault(next.ServiceId)?.Name,
            scheduled,
            expected,
            origin.PlatformId is { } platformId ? new PublicPlatformRef(platformId, platformNumber) : null,
            new PublicStationRef(origin.StationId, NameOf(origin.StationId, lookup)));

        return new PublicGuidance(type, $"{message} {sentence}", nextService, NoLaterService: false);
    }

    /// <summary>
    /// The next journey to the same final station that leaves the same origin later, ignoring one
    /// that is cancelled, terminated or has already completed, and taking the earliest that remains
    /// (design D6). A pure selection over the candidates so the rule is testable on its own.
    /// </summary>
    private static LiveJourneyView? SelectNextService(
        LiveJourneyView affected,
        IReadOnlyList<LiveJourneyView> sameDayJourneys)
    {
        var affectedOrigin = FirstCallingStop(affected);
        var origin = affectedOrigin?.StationId;
        var destination = LastCallingStop(affected)?.StationId;
        var departure = ScheduledDeparture(affectedOrigin);

        if (origin is null || destination is null || departure is null)
        {
            return null;
        }

        return sameDayJourneys
            .Where(candidate => candidate.JourneyId != affected.JourneyId)
            .Where(candidate => candidate.Status is not (
                JourneyLifecycleStatus.Cancelled
                or JourneyLifecycleStatus.Terminated
                or JourneyLifecycleStatus.Completed))
            .Where(candidate => FirstCallingStop(candidate)?.StationId == origin
                && LastCallingStop(candidate)?.StationId == destination
                && ScheduledDeparture(FirstCallingStop(candidate)) > departure)
            .OrderBy(candidate => ScheduledDeparture(FirstCallingStop(candidate)))
            .FirstOrDefault();
    }

    /// <summary>The first calling stop, which is the journey's origin.</summary>
    private static LiveStopView? FirstCallingStop(LiveJourneyView journey) =>
        journey.Stops.Where(stop => stop.IsCalling).MinBy(stop => stop.Sequence);

    /// <summary>The last calling stop, which is the journey's destination.</summary>
    private static LiveStopView? LastCallingStop(LiveJourneyView journey) =>
        journey.Stops.Where(stop => stop.IsCalling).MaxBy(stop => stop.Sequence);

    /// <summary>
    /// The last calling stop the train served: the live state gives no expected times to any stop
    /// after the one a termination ends the journey at.
    /// </summary>
    private static LiveStopView? LastServedStop(LiveJourneyView journey) =>
        journey.Stops
            .Where(stop => stop.IsCalling
                && (stop.ExpectedArrival is not null
                    || stop.ExpectedDeparture is not null
                    || stop.ActualDeparture is not null))
            .MaxBy(stop => stop.Sequence);

    /// <summary>The earliest calling stop whose platform changed and that has not departed yet.</summary>
    private static LiveStopView? FirstChangedStop(LiveJourneyView journey) =>
        journey.Stops
            .Where(stop => stop.IsCalling
                && stop.PlatformChangedAt is not null
                && stop.ActualDeparture is null)
            .MinBy(stop => stop.Sequence);

    private static DateTimeOffset? ScheduledDeparture(LiveStopView? stop) =>
        stop?.ScheduledDeparture ?? stop?.ScheduledArrival;

    private static string? NameOf(Guid? stationId, NetworkLookup lookup) =>
        stationId is { } id ? lookup.Stations.GetValueOrDefault(id)?.Name : null;

    private static string? PlatformNumber(Guid? platformId, NetworkLookup lookup) =>
        platformId is { } id ? lookup.Platforms.GetValueOrDefault(id)?.Number : null;

    /// <summary>The service as a passenger names it: its time, its line and where it goes.</summary>
    private static string Train(string departure, string? lineName, string destination) =>
        string.IsNullOrWhiteSpace(lineName)
            ? $"{departure} service to {destination}"
            : $"{departure} {lineName} service to {destination}";

    /// <summary>The instant as the railway's clock reads it, 24-hour in the configured zone.</summary>
    private static string Time(DateTimeOffset instant, TimeZoneInfo timeZone) =>
        TimeZoneInfo.ConvertTime(instant, timeZone).ToString(TimeFormat, CultureInfo.InvariantCulture);
}
