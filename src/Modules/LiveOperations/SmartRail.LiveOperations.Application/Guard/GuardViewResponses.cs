using SmartRail.LiveOperations.Application.ControllerView;
using SmartRail.LiveOperations.Contracts;
using SmartRail.RailNetwork.Contracts;

namespace SmartRail.LiveOperations.Application.Guard;

/// <summary>
/// One of the calling guard's journeys, with the stops that guard works against. A guard sees only
/// their own journeys, so no other guard and no staff identifier appears here (guard-assignments
/// spec, GRD-01).
/// </summary>
/// <param name="Id">The journey's identifier.</param>
/// <param name="ServiceName">The service's name, or <see langword="null"/> when the timetable no longer holds the journey.</param>
/// <param name="LineCode">The line's code, or <see langword="null"/> when the timetable no longer holds the journey.</param>
/// <param name="LineName">The line's name, or <see langword="null"/> when the timetable no longer holds the journey.</param>
/// <param name="Direction">The direction the service runs in, or <see langword="null"/> when the timetable no longer holds the journey.</param>
/// <param name="Status">The journey's lifecycle status.</param>
/// <param name="DelayMinutes">Minutes early (negative) or late (positive) at the next calling stop.</param>
/// <param name="LastUpdatedAt">When the journey's live state last changed.</param>
/// <param name="Stops">The journey's calling stops, in sequence order; a stop the train passes without calling is omitted.</param>
public sealed record GuardJourneyResponse(
    Guid Id,
    string? ServiceName,
    string? LineCode,
    string? LineName,
    ServiceDirection? Direction,
    JourneyLifecycleStatus Status,
    int? DelayMinutes,
    DateTimeOffset LastUpdatedAt,
    IReadOnlyList<GuardStopResponse> Stops);

/// <summary>One journey with the changes published on it, as the guard sees them.</summary>
/// <param name="Journey">The journey, exactly as the list returns it.</param>
/// <param name="Changes">The changes published on the journey, in publish order.</param>
public sealed record GuardJourneyDetailResponse(
    GuardJourneyResponse Journey,
    IReadOnlyList<GuardChangeResponse> Changes);

/// <summary>
/// One calling stop with its scheduled, expected and actual times and its current platform. The
/// final stop has no departure to log, so <paramref name="IsFinalStop"/> tells the guard's app not
/// to offer one.
/// </summary>
/// <param name="Sequence">The stop's place in the journey.</param>
/// <param name="Station">The station the stop is at.</param>
/// <param name="ScheduledArrival">The timetabled arrival, when the stop has one.</param>
/// <param name="ScheduledDeparture">The timetabled departure, when the stop has one.</param>
/// <param name="ExpectedArrival">The arrival the live state currently expects.</param>
/// <param name="ExpectedDeparture">The departure the live state currently expects.</param>
/// <param name="ActualDeparture">The departure the guard recorded, when it has been recorded.</param>
/// <param name="Platform">The platform the stop uses now, after any uncorrected platform change.</param>
/// <param name="PlatformChangedAt">When the current platform was published; <see langword="null"/> for the timetabled platform.</param>
/// <param name="DepartureLogged">True when a departure has been recorded for this stop.</param>
/// <param name="IsFinalStop">True when this is the journey's last calling stop.</param>
public sealed record GuardStopResponse(
    int Sequence,
    StationRef? Station,
    DateTimeOffset? ScheduledArrival,
    DateTimeOffset? ScheduledDeparture,
    DateTimeOffset? ExpectedArrival,
    DateTimeOffset? ExpectedDeparture,
    DateTimeOffset? ActualDeparture,
    PlatformRef? Platform,
    DateTimeOffset? PlatformChangedAt,
    bool DepartureLogged,
    bool IsFinalStop);

/// <summary>
/// One change published on the journey, as the guard's view shows it. It carries no publisher: the
/// guard needs to know what changed and when, not who published it (guard-assignments spec,
/// GRD-08).
/// </summary>
/// <param name="Id">The change's identifier.</param>
/// <param name="Type">The kind of change, for example a delay, a cancellation or a platform change.</param>
/// <param name="Values">The values the change set, keyed by field name.</param>
/// <param name="PublishedAt">When the change was published.</param>
/// <param name="Corrected">True when a later correction has withdrawn the change.</param>
public sealed record GuardChangeResponse(
    Guid Id,
    string Type,
    IReadOnlyDictionary<string, string?> Values,
    DateTimeOffset PublishedAt,
    bool Corrected);
