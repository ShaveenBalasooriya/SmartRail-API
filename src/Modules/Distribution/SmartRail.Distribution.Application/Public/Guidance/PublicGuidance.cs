namespace SmartRail.Distribution.Application.Public.Guidance;

/// <summary>The kind of disruption a guidance message explains (disruption-guidance spec).</summary>
public enum PublicGuidanceType
{
    /// <summary>The journey will not run at all.</summary>
    Cancellation,

    /// <summary>The journey ended early, before its terminus.</summary>
    Termination,

    /// <summary>The journey departs from a different platform than the timetable says.</summary>
    PlatformChange,

    /// <summary>The journey runs late, by at least the guidance threshold.</summary>
    Delay,
}

/// <summary>
/// The plain-language guidance a disrupted journey carries, written from fixed templates in code and
/// never free-form or AI-generated (disruption-guidance spec, design D1). A journey carries one
/// guidance object even when several disruptions apply at once: a cancellation is reported before an
/// early termination, then a platform change, then a delay (design D-11).
/// </summary>
/// <param name="Type">Which disruption the message is about.</param>
/// <param name="Message">
/// The complete sentence a passenger reads. It names what happened and what to do without relying on
/// colour, an icon or its position, so a screen reader conveys the same meaning (M-05, PAX-11).
/// </param>
/// <param name="NextService">
/// The next journey to the same destination, for a cancellation, an early termination or a delay of
/// fifteen minutes or more; <see langword="null"/> for every other disruption and when no later
/// service runs today.
/// </param>
/// <param name="NoLaterService">
/// True when guidance names a next service and none runs later today, so the message says so.
/// </param>
public sealed record PublicGuidance(
    PublicGuidanceType Type,
    string Message,
    PublicNextService? NextService,
    bool NoLaterService);

/// <summary>
/// The next journey a passenger can take to the destination their disrupted journey no longer
/// reaches: the earliest later departure from the same origin to the same final station that is
/// neither cancelled nor terminated and has not already gone (design D6).
/// </summary>
/// <param name="JourneyId">The next journey's identifier.</param>
/// <param name="ServiceName">The next journey's service name, or <see langword="null"/> when the timetable no longer holds the service.</param>
/// <param name="ScheduledDeparture">The next journey's timetabled departure from the shared origin.</param>
/// <param name="ExpectedDeparture">The departure the live state currently expects at that origin.</param>
/// <param name="Platform">The platform the next journey leaves from, or <see langword="null"/> when the catalogue has none.</param>
/// <param name="Station">The origin station both journeys call at.</param>
public sealed record PublicNextService(
    Guid JourneyId,
    string? ServiceName,
    DateTimeOffset ScheduledDeparture,
    DateTimeOffset ExpectedDeparture,
    PublicPlatformRef? Platform,
    PublicStationRef Station);
