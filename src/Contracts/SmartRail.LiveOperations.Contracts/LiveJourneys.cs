namespace SmartRail.LiveOperations.Contracts;

/// <summary>
/// Read access to the live state of journeys and to the journey event stream (architecture §3,
/// design D6). Other modules use this instead of reading the <c>operations</c> schema.
/// </summary>
public interface ILiveJourneys
{
    /// <summary>One journey with its live stops, or <see langword="null"/> when the ID is unknown.</summary>
    Task<LiveJourneyView?> GetAsync(Guid journeyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The live state of every journey on one railway calendar day, ordered by scheduled departure.
    /// </summary>
    Task<IReadOnlyList<LiveJourneyView>> GetByDateAsync(
        DateOnly serviceDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The events whose stream position is greater than <paramref name="position"/>, in position
    /// order, optionally restricted to one journey. <paramref name="limit"/> bounds the batch so a
    /// catch-up can be paged.
    /// </summary>
    Task<IReadOnlyList<JourneyEventView>> GetEventsAfterAsync(
        long position,
        Guid? journeyId = null,
        int limit = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The lowest stream position still retained, or <see langword="null"/> when no event exists.
    /// A client that last saw a position below this one has missed events that are no longer
    /// available and must reset (live-distribution design D5).
    /// </summary>
    Task<long?> GetOldestRetainedPositionAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The live picture of one journey, and the snapshot a journey event carries (design D6). It holds
/// the identifiers the live state stores; service names, line names and direction come from the
/// Rail Network contract.
/// </summary>
public sealed record LiveJourneyView(
    Guid JourneyId,
    Guid ServiceId,
    Guid LineId,
    DateOnly ServiceDate,
    JourneyLifecycleStatus Status,
    int? DelayMinutes,
    Guid? LastDepartedStationId,
    Guid? NextStationId,
    DateTimeOffset LastUpdatedAt,
    Guid GuardStaffId,
    IReadOnlyList<LiveStopView> Stops);

/// <summary>
/// One stop's live data, in sequence order. A non-calling stop keeps its station but has no
/// scheduled times; a cancelled journey reports scheduled times but no expected times.
/// </summary>
public sealed record LiveStopView(
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

/// <summary>
/// One event of the persisted journey event stream (journey-event-stream spec).
/// <see cref="SourceId"/> is the departure log or status change that produced the event;
/// <see cref="Snapshot"/> is the journey's live state immediately after the event was committed.
/// </summary>
public sealed record JourneyEventView(
    Guid Id,
    JourneyEventType Type,
    Guid JourneyId,
    long SequenceNumber,
    long StreamPosition,
    Guid? SourceId,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt,
    LiveJourneyView Snapshot);
