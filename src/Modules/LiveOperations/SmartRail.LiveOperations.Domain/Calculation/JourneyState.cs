namespace SmartRail.LiveOperations.Domain.Calculation;

/// <summary>Where a journey is in its lifecycle.</summary>
public enum JourneyStatus
{
    /// <summary>No departure has been recorded and no cancellation is in force.</summary>
    Scheduled,

    /// <summary>At least one departure has been recorded.</summary>
    Running,

    /// <summary>An uncorrected cancellation is in force.</summary>
    Cancelled,

    /// <summary>
    /// An uncorrected termination is in force: the train ended early, before its terminus (design
    /// D7).
    /// </summary>
    Terminated,

    /// <summary>The penultimate calling stop has departed and the terminus arrival has passed.</summary>
    Completed,
}

/// <summary>
/// The computed live picture of one journey: status, minutes early or late, progress, freshness and
/// the per-stop expected and actual times and platform.
/// </summary>
public sealed record JourneyState(
    Guid JourneyId,
    JourneyStatus Status,
    int? DelayMinutes,
    Guid? LastDepartedStationId,
    Guid? NextStationId,
    DateTimeOffset LastUpdatedAt,
    IReadOnlyList<StopState> Stops);

/// <summary>
/// One stop's live data. A cancelled journey reports the scheduled times but no expected times.
/// </summary>
public sealed record StopState(
    int Sequence,
    Guid StationId,
    bool IsCalling,
    DateTimeOffset? ScheduledArrival,
    DateTimeOffset? ScheduledDeparture,
    DateTimeOffset? ExpectedArrival,
    DateTimeOffset? ExpectedDeparture,
    DateTimeOffset? ActualDeparture,
    Guid? PlatformId,
    DateTimeOffset? PlatformChangedAt);
