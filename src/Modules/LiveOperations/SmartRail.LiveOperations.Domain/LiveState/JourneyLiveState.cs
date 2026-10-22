using SmartRail.LiveOperations.Domain.Calculation;
using SmartRail.SharedKernel.Primitives;
using SmartRail.SharedKernel.Results;

namespace SmartRail.LiveOperations.Domain.LiveState;

/// <summary>
/// One journey's live state, the journey-level half of the picture every channel reads. Its
/// identifier is the journey's identifier: there is exactly one state row per journey. The
/// aggregate owns its stops and recomputes itself from the facts recorded for it (design D4).
/// </summary>
public sealed class JourneyLiveState : Entity<Guid>
{
    private JourneyLiveState()
    {
    }

    private JourneyLiveState(
        Guid journeyId,
        Guid serviceId,
        Guid lineId,
        DateOnly serviceDate,
        Guid guardStaffId,
        JourneyLifecycleStatus status,
        int? delayMinutes,
        Guid? lastDepartedStationId,
        Guid? nextStationId,
        long lastSequenceNumber,
        DateTimeOffset lastUpdatedAt)
        : base(journeyId)
    {
        ServiceId = serviceId;
        LineId = lineId;
        ServiceDate = serviceDate;
        GuardStaffId = guardStaffId;
        Status = status;
        DelayMinutes = delayMinutes;
        LastDepartedStationId = lastDepartedStationId;
        NextStationId = nextStationId;
        LastSequenceNumber = lastSequenceNumber;
        LastUpdatedAt = lastUpdatedAt;
    }

    public Guid ServiceId { get; private set; }

    public Guid LineId { get; private set; }

    public DateOnly ServiceDate { get; private set; }

    public Guid GuardStaffId { get; private set; }

    public JourneyLifecycleStatus Status { get; private set; }

    /// <summary>Minutes early (negative) or late (positive), taken from the next calling stop.</summary>
    public int? DelayMinutes { get; private set; }

    public Guid? LastDepartedStationId { get; private set; }

    public Guid? NextStationId { get; private set; }

    /// <summary>The highest per-journey sequence number appended so far; zero before the first event.</summary>
    public long LastSequenceNumber { get; private set; }

    /// <summary>The freshness time shown on every channel.</summary>
    public DateTimeOffset LastUpdatedAt { get; private set; }

    public ICollection<JourneyStopLiveState> Stops { get; } = [];

    /// <summary>
    /// Builds the initial state of a journey from its timetable baseline: status <c>scheduled</c>,
    /// expected times equal to scheduled times, the baseline platforms and a last-updated time equal
    /// to the creation time (live-running-state spec). It is computed through the same calculator a
    /// later recomputation uses, so the created state and a recomputation with no facts cannot drift
    /// apart.
    /// </summary>
    public static Result<JourneyLiveState> Create(
        Baseline baseline,
        Guid serviceId,
        Guid lineId,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(baseline);

        if (baseline.JourneyId == Guid.Empty)
        {
            return Result.Failure<JourneyLiveState>(JourneyLiveStateErrors.MissingJourneyId);
        }

        if (serviceId == Guid.Empty)
        {
            return Result.Failure<JourneyLiveState>(JourneyLiveStateErrors.MissingServiceId);
        }

        if (lineId == Guid.Empty)
        {
            return Result.Failure<JourneyLiveState>(JourneyLiveStateErrors.MissingLineId);
        }

        if (baseline.Stops.Count == 0)
        {
            return Result.Failure<JourneyLiveState>(JourneyLiveStateErrors.MissingStops);
        }

        var computed = LiveStateCalculator.Compute(baseline, [], now);

        var state = new JourneyLiveState(
            baseline.JourneyId,
            serviceId,
            lineId,
            baseline.ServiceDate,
            baseline.GuardStaffId,
            MapStatus(computed.Status),
            computed.DelayMinutes,
            computed.LastDepartedStationId,
            computed.NextStationId,
            lastSequenceNumber: 0,
            computed.LastUpdatedAt);

        foreach (var stop in computed.Stops.OrderBy(stop => stop.Sequence))
        {
            state.Stops.Add(JourneyStopLiveState.Create(baseline.JourneyId, stop));
        }

        return Result.Success(state);
    }

    /// <summary>
    /// Recomputes the journey from its baseline and every fact recorded for it, updates the state and
    /// stop rows, and reports whether anything changed. The caller records an event only when this
    /// returns <see langword="true"/>; a fact that changes nothing therefore consumes no sequence
    /// number and appends no event (journey-event-stream spec).
    /// </summary>
    public bool Apply(IReadOnlyList<Fact> facts, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(facts);

        var computed = LiveStateCalculator.Compute(CreateBaseline(), facts, now);
        var status = MapStatus(computed.Status);
        var lastUpdatedAt = Truncate(computed.LastUpdatedAt);

        // PostgreSQL keeps timestamps to the microsecond, so every derived timestamp is rounded down
        // before it is compared or stored. Otherwise a replayed fact whose timestamp has
        // sub-microsecond ticks would look like a change on every application.
        var stops = computed.Stops
            .OrderBy(stop => stop.Sequence)
            .Select(stop => stop with
            {
                ExpectedArrival = Truncate(stop.ExpectedArrival),
                ExpectedDeparture = Truncate(stop.ExpectedDeparture),
                ActualDeparture = Truncate(stop.ActualDeparture),
                PlatformChangedAt = Truncate(stop.PlatformChangedAt),
            })
            .ToArray();

        if (!HasChanged(status, computed.DelayMinutes, computed.LastDepartedStationId, computed.NextStationId, lastUpdatedAt, stops))
        {
            return false;
        }

        Status = status;
        DelayMinutes = computed.DelayMinutes;
        LastDepartedStationId = computed.LastDepartedStationId;
        NextStationId = computed.NextStationId;
        LastUpdatedAt = lastUpdatedAt;

        foreach (var stop in stops)
        {
            // The scheduled values on the row are the baseline and are never rewritten.
            Stops.Single(state => state.Sequence == stop.Sequence).Update(
                stop.ExpectedArrival,
                stop.ExpectedDeparture,
                stop.ActualDeparture,
                stop.PlatformId,
                stop.PlatformChangedAt);
        }

        return true;
    }

    /// <summary>
    /// Builds the event that records one committed change of this journey. It enforces the stream's
    /// rule that a journey's events are numbered 1, 2, 3 with no gaps, so it is the only place that
    /// consumes the next sequence number; the caller supplies the snapshot of the state
    /// <see cref="Apply"/> has just written.
    /// </summary>
    public JourneyEvent RecordEvent(
        JourneyEventType eventType,
        Guid? sourceId,
        DateTimeOffset occurredAt,
        DateTimeOffset recordedAt,
        string snapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshot);

        var sequenceNumber = LastSequenceNumber + 1;
        LastSequenceNumber = sequenceNumber;

        return JourneyEvent.Create(
            Id,
            sequenceNumber,
            eventType,
            sourceId,
            snapshot,
            occurredAt,
            recordedAt);
    }

    private Baseline CreateBaseline() => new(
        Id,
        ServiceDate,
        GuardStaffId,
        Stops
            .OrderBy(stop => stop.Sequence)
            .Select(stop => new BaselineStop(
                stop.Sequence,
                stop.StationId,
                stop.IsCalling,
                stop.ScheduledArrival,
                stop.ScheduledDeparture,
                stop.PlatformId))
            .ToArray(),
        // Before any fact the stored freshness is the journey's creation time, which is the
        // calculator's fallback.
        LastUpdatedAt);

    private bool HasChanged(
        JourneyLifecycleStatus status,
        int? delayMinutes,
        Guid? lastDepartedStationId,
        Guid? nextStationId,
        DateTimeOffset lastUpdatedAt,
        StopState[] stops)
    {
        if (Status != status
            || DelayMinutes != delayMinutes
            || LastDepartedStationId != lastDepartedStationId
            || NextStationId != nextStationId
            || LastUpdatedAt != lastUpdatedAt
            || Stops.Count != stops.Length)
        {
            return true;
        }

        foreach (var stop in stops)
        {
            var stored = Stops.Single(state => state.Sequence == stop.Sequence);

            if (stored.ExpectedArrival != stop.ExpectedArrival
                || stored.ExpectedDeparture != stop.ExpectedDeparture
                || stored.ActualDeparture != stop.ActualDeparture
                || stored.PlatformId != stop.PlatformId
                || stored.PlatformChangedAt != stop.PlatformChangedAt)
            {
                return true;
            }
        }

        return false;
    }

    private static JourneyLifecycleStatus MapStatus(JourneyStatus status) => status switch
    {
        JourneyStatus.Scheduled => JourneyLifecycleStatus.Scheduled,
        JourneyStatus.Running => JourneyLifecycleStatus.Running,
        JourneyStatus.Cancelled => JourneyLifecycleStatus.Cancelled,
        JourneyStatus.Completed => JourneyLifecycleStatus.Completed,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown journey status."),
    };

    private static DateTimeOffset Truncate(DateTimeOffset value)
    {
        return new DateTimeOffset(
            value.UtcTicks - (value.UtcTicks % TimeSpan.TicksPerMicrosecond),
            TimeSpan.Zero);
    }

    private static DateTimeOffset? Truncate(DateTimeOffset? value)
    {
        return value is { } timestamp ? Truncate(timestamp) : null;
    }
}
