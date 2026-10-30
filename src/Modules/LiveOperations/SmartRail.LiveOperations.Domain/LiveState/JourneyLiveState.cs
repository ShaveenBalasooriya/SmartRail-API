using SmartRail.LiveOperations.Domain.Calculation;
using SmartRail.LiveOperations.Domain.Changes;
using SmartRail.LiveOperations.Domain.Departures;
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

    /// <summary>
    /// The publish time of the most recent correction applied to the journey (decision D-10), or
    /// <see langword="null"/> for a journey that was never corrected. It stays set afterwards: it is
    /// the note that the journey was corrected (PAX-12), not a flag on the change in force.
    /// </summary>
    public DateTimeOffset? CorrectedAt { get; private set; }

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
    /// stop rows, and reports whether anything changed. The caller records an event when this returns
    /// <see langword="true"/>; a fact that changes nothing consumes no sequence number and appends no
    /// event (journey-event-stream spec), except a published change, which always appends one so every
    /// channel receives it (change-correction spec).
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
    /// Notes that the journey was corrected at <paramref name="correctedAt"/> (decision D-10): the
    /// publish time of the correction becomes the journey's corrected time and stays there, so a
    /// journey that was corrected can say so afterwards. The time only moves forward, so replaying an
    /// older correction cannot hide a newer one. It reports whether the value changed; the caller
    /// still records the correction's event either way.
    /// </summary>
    public bool MarkCorrected(DateTimeOffset correctedAt)
    {
        // PostgreSQL keeps timestamps to the microsecond, so the value is rounded down here, the way
        // every other stored timestamp of the state is. The snapshot and the stored row then agree.
        var timestamp = Truncate(correctedAt);

        if (CorrectedAt is { } existing && timestamp <= existing)
        {
            return false;
        }

        CorrectedAt = timestamp;
        return true;
    }

    /// <summary>
    /// Whether the journey is running and its freshness time is older than <paramref name="threshold"/>
    /// at <paramref name="now"/> (NC-02). Only a running journey can be stale: a scheduled or cancelled
    /// journey has no guard reporting progress, and a completed one is finished.
    /// </summary>
    public bool IsStaleAt(DateTimeOffset now, TimeSpan threshold)
    {
        return Status == JourneyLifecycleStatus.Running && now - LastUpdatedAt > threshold;
    }

    /// <summary>
    /// Whether a change may still be published on the journey (status-publication spec): a scheduled
    /// or running journey is open, while a cancelled or completed one is closed.
    /// </summary>
    public bool IsOpenForChanges => Status is JourneyLifecycleStatus.Scheduled or JourneyLifecycleStatus.Running;

    /// <summary>
    /// Whether the Guard has recorded a departure anywhere on the journey. Once one exists the
    /// journey is running, whatever its cached status says, and it is ended as an early termination
    /// rather than cancelled.
    /// </summary>
    public bool HasRecordedDeparture => Stops.Any(stop => stop.ActualDeparture is not null);

    /// <summary>The stop at <paramref name="sequence"/>, or <see langword="null"/> when the journey has none.</summary>
    public JourneyStopLiveState? StopAt(int sequence) =>
        Stops.FirstOrDefault(stop => stop.Sequence == sequence);

    /// <summary>
    /// Checks a cancellation against the state (NC-04): only a journey with no recorded departure may
    /// be cancelled. A journey that has already departed is refused with a conflict, because ending a
    /// running journey is an early termination, published separately (design D7).
    /// </summary>
    public Result CheckCanBeCancelled()
    {
        return HasRecordedDeparture
            ? Result.Failure(StatusChangeErrors.CancellationAfterDeparture)
            : Result.Success();
    }

    /// <summary>
    /// Checks a departure log's stop against the state (departure-logging spec, "Only valid stops"):
    /// the sequence has to exist on the journey, it has to be a calling stop, and it cannot be the
    /// journey's final calling stop, which is arrived at rather than departed. The timestamp rules
    /// and the time order against already recorded departures are held by
    /// <see cref="DepartureLogRules"/>.
    /// </summary>
    public Result CheckDepartureCanBeLogged(int stopSequence)
    {
        var stop = StopAt(stopSequence);

        if (stop is null)
        {
            return Result.Failure(DepartureLogErrors.StopNotFound(stopSequence));
        }

        if (!stop.IsCalling)
        {
            return Result.Failure(DepartureLogErrors.StopNotCalling(stopSequence));
        }

        return stopSequence == LastCallingStopSequence()
            ? Result.Failure(DepartureLogErrors.StopIsFinalStop)
            : Result.Success();
    }

    /// <summary>
    /// Checks a platform change's stop against the state (NC-05): it has to be a calling stop that has
    /// not departed, and the change has to move it to a different platform. Whether the platform
    /// belongs to the stop's station needs the network catalogue and is checked by the command.
    /// </summary>
    public Result CheckPlatformCanChange(int stopSequence, Guid newPlatformId)
    {
        var stop = StopAt(stopSequence);

        if (stop is null || !stop.IsCalling)
        {
            return Result.Failure(StatusChangeErrors.PlatformStopNotCalling);
        }

        if (stop.ActualDeparture is not null)
        {
            return Result.Failure(StatusChangeErrors.PlatformStopAlreadyDeparted);
        }

        return stop.PlatformId == newPlatformId
            ? Result.Failure(StatusChangeErrors.PlatformUnchanged)
            : Result.Success();
    }

    /// <summary>
    /// Checks a delay's starting stop against the state (NC-03): it has to be a calling stop that has
    /// not departed, so a delay can never rewrite what has already happened. The range of the delay
    /// itself is a shape rule of <see cref="StatusChange.PublishDelay"/>.
    /// </summary>
    public Result CheckDelayCanStart(int stopSequence)
    {
        var stop = StopAt(stopSequence);

        if (stop is null || !stop.IsCalling)
        {
            return Result.Failure(StatusChangeErrors.DelayStopNotCalling);
        }

        return stop.ActualDeparture is null
            ? Result.Success()
            : Result.Failure(StatusChangeErrors.DelayStopAlreadyDeparted);
    }

    /// <summary>
    /// Checks an early termination against the state (design D7): only a running journey can be ended
    /// before its terminus, at a calling stop that is not its final one and not before the last stop
    /// the Guard has departed, because that departure already happened. A journey that has not
    /// started is cancelled instead, and a journey already closed to changes is refused by the
    /// pipeline.
    /// </summary>
    public Result CheckCanBeTerminated(int lastStopSequence)
    {
        if (Status == JourneyLifecycleStatus.Scheduled)
        {
            return Result.Failure(StatusChangeErrors.TerminationNotStarted);
        }

        if (Status != JourneyLifecycleStatus.Running)
        {
            return Result.Failure(StatusChangeErrors.JourneyClosed);
        }

        var stop = StopAt(lastStopSequence);

        if (stop is null || !stop.IsCalling)
        {
            return Result.Failure(StatusChangeErrors.TerminationStopNotCalling);
        }

        if (lastStopSequence == LastCallingStopSequence())
        {
            return Result.Failure(StatusChangeErrors.TerminationAtFinalStop);
        }

        return lastStopSequence < LastDepartedStopSequence()
            ? Result.Failure(StatusChangeErrors.TerminationBeforeLastDeparture)
            : Result.Success();
    }

    /// <summary>
    /// The delay in whole minutes the stop shows before a change, which is the before value of a
    /// published delay's audit entry (design D4). A stop with no expected time shows no delay.
    /// </summary>
    public int DelayMinutesAt(int stopSequence)
    {
        var stop = StopAt(stopSequence);
        var scheduled = stop?.ScheduledDeparture ?? stop?.ScheduledArrival;
        var expected = stop?.ExpectedDeparture ?? stop?.ExpectedArrival;

        if (scheduled is null || expected is null)
        {
            return 0;
        }

        return (int)Math.Round((expected.Value - scheduled.Value).TotalMinutes, MidpointRounding.AwayFromZero);
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

    /// <summary>The sequence of the journey's last calling stop, or zero when it calls nowhere.</summary>
    private int LastCallingStopSequence() => Stops
        .Where(stop => stop.IsCalling)
        .Select(stop => stop.Sequence)
        .DefaultIfEmpty(0)
        .Max();

    /// <summary>The sequence of the highest stop with a recorded departure, or zero when there is none.</summary>
    private int LastDepartedStopSequence() => Stops
        .Where(stop => stop.ActualDeparture is not null)
        .Select(stop => stop.Sequence)
        .DefaultIfEmpty(0)
        .Max();

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
        JourneyStatus.Terminated => JourneyLifecycleStatus.Terminated,
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
