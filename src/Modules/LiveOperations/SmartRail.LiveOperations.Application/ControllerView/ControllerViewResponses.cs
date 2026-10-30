using SmartRail.LiveOperations.Contracts;
using SmartRail.RailNetwork.Contracts;

namespace SmartRail.LiveOperations.Application.ControllerView;

/// <summary>
/// One of today's journeys as the Network Controller's live view lists it. The guard is named, never
/// identified, because staff IDs stay inside staff-only responses that name the account's role.
/// </summary>
/// <param name="Id">The journey's identifier.</param>
/// <param name="ServiceName">The service's name, or <see langword="null"/> when the timetable no longer holds the journey.</param>
/// <param name="LineCode">The line's code, or <see langword="null"/> when the timetable no longer holds the journey.</param>
/// <param name="LineName">The line's name, or <see langword="null"/> when the timetable no longer holds the journey.</param>
/// <param name="Direction">The direction the service runs in, or <see langword="null"/> when the timetable no longer holds the journey.</param>
/// <param name="Status">The journey's lifecycle status.</param>
/// <param name="DelayMinutes">Minutes early (negative) or late (positive) at the next calling stop.</param>
/// <param name="LastDepartedStation">The station of the last recorded departure, if any.</param>
/// <param name="NextStation">The station the journey calls at next, if any.</param>
/// <param name="GuardName">The assigned guard's display name, if the account exists.</param>
/// <param name="LastUpdatedAt">When the journey's live state last changed.</param>
/// <param name="IsStale">True when the journey is running and has not been updated within the stale threshold.</param>
public sealed record ControllerJourneyResponse(
    Guid Id,
    string? ServiceName,
    string? LineCode,
    string? LineName,
    ServiceDirection? Direction,
    JourneyLifecycleStatus Status,
    int? DelayMinutes,
    StationRef? LastDepartedStation,
    StationRef? NextStation,
    string? GuardName,
    DateTimeOffset LastUpdatedAt,
    bool IsStale);

/// <summary>One journey with every stop and the changes published on it.</summary>
/// <param name="Journey">The journey's summary, as the list returns it.</param>
/// <param name="Stops">Every stop in sequence order.</param>
/// <param name="Changes">The changes published on the journey, in publish order.</param>
public sealed record ControllerJourneyDetailResponse(
    ControllerJourneyResponse Journey,
    IReadOnlyList<ControllerStopResponse> Stops,
    IReadOnlyList<ControllerChangeResponse> Changes);

/// <summary>One stop with its scheduled, expected and actual times and its current platform.</summary>
/// <param name="Sequence">The stop's place in the journey.</param>
/// <param name="Station">The station the stop is at.</param>
/// <param name="IsCalling">False for a stop the journey passes without calling; such a stop has no times.</param>
/// <param name="ScheduledArrival">The timetabled arrival, when the stop is a calling stop with one.</param>
/// <param name="ScheduledDeparture">The timetabled departure, when the stop is a calling stop with one.</param>
/// <param name="ExpectedArrival">The arrival the live state currently expects.</param>
/// <param name="ExpectedDeparture">The departure the live state currently expects.</param>
/// <param name="ActualDeparture">The departure the Guard recorded, when it has been recorded.</param>
/// <param name="Platform">The platform the stop uses now, after any uncorrected platform change.</param>
public sealed record ControllerStopResponse(
    int Sequence,
    StationRef? Station,
    bool IsCalling,
    DateTimeOffset? ScheduledArrival,
    DateTimeOffset? ScheduledDeparture,
    DateTimeOffset? ExpectedArrival,
    DateTimeOffset? ExpectedDeparture,
    DateTimeOffset? ActualDeparture,
    PlatformRef? Platform);

/// <summary>One change published on a journey, as the controller's history shows it.</summary>
/// <param name="Id">The change's identifier.</param>
/// <param name="Type">The kind of change, for example a delay, a cancellation or a platform change.</param>
/// <param name="Values">The values the change set, keyed by field name.</param>
/// <param name="PublisherName">The display name of the controller who published the change, if the account exists.</param>
/// <param name="PublishedAt">When the change was published.</param>
/// <param name="Provenance">The label that says how the change reached the channels.</param>
/// <param name="Corrected">True when a later correction has withdrawn the change.</param>
public sealed record ControllerChangeResponse(
    Guid Id,
    string Type,
    IReadOnlyDictionary<string, string?> Values,
    string? PublisherName,
    DateTimeOffset PublishedAt,
    string Provenance,
    bool Corrected);

/// <summary>A station by identifier and display name.</summary>
public sealed record StationRef(Guid Id, string Name);

/// <summary>A platform by identifier and the number a station's passengers see.</summary>
public sealed record PlatformRef(Guid Id, string Number);
