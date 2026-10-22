namespace SmartRail.LiveOperations.Domain.Calculation;

/// <summary>
/// The timetable baseline a journey's live state is computed from: the journey's identity, the
/// assigned guard, its stops in running order with their scheduled times as UTC instants, and the
/// time the journey was created (the freshness fallback before any fact is recorded).
/// </summary>
public sealed record Baseline(
    Guid JourneyId,
    DateOnly ServiceDate,
    Guid GuardStaffId,
    IReadOnlyList<BaselineStop> Stops,
    DateTimeOffset CreatedAt);

/// <summary>
/// One stop of the baseline. A non-calling stop has no scheduled times and no platform; an origin
/// has no scheduled arrival and a terminus has no scheduled departure.
/// </summary>
public sealed record BaselineStop(
    int Sequence,
    Guid StationId,
    bool IsCalling,
    DateTimeOffset? ScheduledArrival,
    DateTimeOffset? ScheduledDeparture,
    Guid? PlatformId);
