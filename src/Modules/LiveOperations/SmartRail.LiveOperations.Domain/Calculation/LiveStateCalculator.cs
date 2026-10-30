namespace SmartRail.LiveOperations.Domain.Calculation;

/// <summary>
/// Computes a journey's live state from its baseline and the facts recorded so far (design D1).
/// The calculation is pure and deterministic: the same facts always produce the same state, and a
/// correction is handled by leaving the corrected fact out of the input rather than undoing it.
/// </summary>
public static class LiveStateCalculator
{
    /// <summary>
    /// Computes the state at <paramref name="now"/>. <paramref name="now"/> is passed in rather than
    /// read from a clock so completion is a function of its inputs, like everything else here.
    /// </summary>
    public static JourneyState Compute(Baseline baseline, IReadOnlyList<Fact> facts, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(facts);

        var stops = baseline.Stops.OrderBy(stop => stop.Sequence).ToArray();
        var stopsBySequence = stops.ToDictionary(stop => stop.Sequence);

        var departures = facts.OfType<DepartureFact>().ToArray();

        // A corrected change is withdrawn: filtering it here is what makes a correction leave the
        // state exactly as it was before the original was published.
        var changes = facts
            .OfType<ChangeFact>()
            .Where(change => !change.Corrected)
            .ToArray();

        var departureByStop = departures
            .GroupBy(departure => departure.StopSequence)
            .ToDictionary(
                group => group.Key,
                group => group.MaxBy(departure => departure.StreamOrder)!);

        var cancelled = changes.Any(change => change.Kind == ChangeKind.Cancellation);

        // A journey ends early at the last stop it served; the most recent termination is the one in
        // force when more than one was published (design D7).
        var termination = changes
            .Where(change => change.Kind == ChangeKind.Termination)
            .MaxBy(change => change.StreamOrder);
        var terminated = termination is not null;

        var delays = new Dictionary<int, int>(stops.Length);
        var stopStates = new StopState[stops.Length];

        for (var index = 0; index < stops.Length; index++)
        {
            var stop = stops[index];
            var actualDeparture = departureByStop.GetValueOrDefault(stop.Sequence)?.DepartedAt;

            // A cancelled journey reports no expected times at all. A terminated one reports them up
            // to the last stop the train served and none after it: the train never reaches those
            // stops, exactly as for a cancelled journey.
            var noExpectedTimes = cancelled
                || (termination is not null
                    && stop.Sequence > (termination.StopSequence ?? int.MinValue));

            // A stop that has departed carries its own deviation; one that has not takes the most
            // recently recorded fact that covers it.
            var delay = actualDeparture is not null
                ? DelayFromDeparture(stop, actualDeparture.Value)
                : CoveringDelay(stop.Sequence, departures, changes, stopsBySequence);

            delays[stop.Sequence] = delay;

            var platformChange = changes
                .Where(change => change.Kind == ChangeKind.PlatformChange
                    && change.StopSequence == stop.Sequence)
                .MaxBy(change => change.StreamOrder);

            stopStates[index] = new StopState(
                stop.Sequence,
                stop.StationId,
                stop.IsCalling,
                stop.ScheduledArrival,
                stop.ScheduledDeparture,
                Expected(stop.ScheduledArrival, delay, noExpectedTimes),
                Expected(stop.ScheduledDeparture, Math.Max(delay, 0), noExpectedTimes),
                actualDeparture,
                platformChange?.PlatformId ?? stop.PlatformId,
                platformChange?.PublishedAt);
        }

        var callingStops = stops.Where(stop => stop.IsCalling).ToArray();

        var lastDeparted = callingStops
            .Where(stop => departureByStop.ContainsKey(stop.Sequence))
            .MaxBy(stop => stop.Sequence);

        var next = callingStops.FirstOrDefault(
            stop => lastDeparted is null || stop.Sequence > lastDeparted.Sequence);

        var completed = !cancelled
            && !terminated
            && HasCompleted(callingStops, departureByStop, stopStates, now);

        JourneyStatus status;
        if (cancelled)
        {
            status = JourneyStatus.Cancelled;
        }
        else if (terminated)
        {
            // Termination takes precedence over completion: the train did not reach its terminus,
            // even if the schedule says the terminus arrival has passed (design D7).
            status = JourneyStatus.Terminated;
        }
        else if (completed)
        {
            status = JourneyStatus.Completed;
        }
        else if (lastDeparted is not null)
        {
            status = JourneyStatus.Running;
        }
        else
        {
            status = JourneyStatus.Scheduled;
        }

        // A cancelled or terminated journey reports no delay and no next station; a finished journey
        // has no next calling stop to take a delay from, so its delay settles at zero.
        int? delayMinutes = cancelled || terminated
            ? null
            : next is null
                ? 0
                : delays[next.Sequence];

        return new JourneyState(
            baseline.JourneyId,
            status,
            delayMinutes,
            lastDeparted?.StationId,
            cancelled || terminated ? null : next?.StationId,
            LatestRecordedAt(departures, changes, baseline.CreatedAt),
            stopStates);
    }

    private static DateTimeOffset? Expected(
        DateTimeOffset? scheduled,
        int delayMinutes,
        bool noExpectedTime)
    {
        if (noExpectedTime || scheduled is null)
        {
            return null;
        }

        return scheduled.Value.AddMinutes(delayMinutes);
    }

    /// <summary>
    /// The delay of the most recently recorded fact covering <paramref name="stopSequence"/>: a
    /// departure at an earlier stop, or a published delay from an earlier or the same stop. Ties on
    /// commitment order keep the first one found, so the result is deterministic.
    /// </summary>
    private static int CoveringDelay(
        int stopSequence,
        IEnumerable<DepartureFact> departures,
        IEnumerable<ChangeFact> changes,
        Dictionary<int, BaselineStop> stopsBySequence)
    {
        var latestOrder = long.MinValue;
        var delay = 0;

        foreach (var departure in departures)
        {
            if (departure.StopSequence > stopSequence
                || departure.StreamOrder <= latestOrder
                || !stopsBySequence.TryGetValue(departure.StopSequence, out var stop))
            {
                continue;
            }

            latestOrder = departure.StreamOrder;
            delay = DelayFromDeparture(stop, departure.DepartedAt);
        }

        foreach (var change in changes)
        {
            if (change.Kind != ChangeKind.Delay
                || change.StreamOrder <= latestOrder
                || (change.StopSequence ?? int.MinValue) > stopSequence)
            {
                continue;
            }

            latestOrder = change.StreamOrder;
            delay = change.DelayMinutes ?? 0;
        }

        return delay;
    }

    /// <summary>The departure's deviation from schedule in whole minutes, rounded to nearest.</summary>
    private static int DelayFromDeparture(BaselineStop stop, DateTimeOffset departedAt)
    {
        var scheduled = stop.ScheduledDeparture ?? stop.ScheduledArrival;
        if (scheduled is null)
        {
            return 0;
        }

        var minutes = (departedAt - scheduled.Value).TotalMinutes;
        return (int)Math.Round(minutes, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// A journey completes once the penultimate calling stop has departed and the expected arrival
    /// at the terminus has passed (design D4).
    /// </summary>
    private static bool HasCompleted(
        BaselineStop[] callingStops,
        Dictionary<int, DepartureFact> departureByStop,
        StopState[] stopStates,
        DateTimeOffset now)
    {
        if (callingStops.Length < 2)
        {
            return false;
        }

        var penultimate = callingStops[^2];
        if (!departureByStop.ContainsKey(penultimate.Sequence))
        {
            return false;
        }

        var terminus = callingStops[^1];
        var terminusArrival = stopStates
            .First(state => state.Sequence == terminus.Sequence)
            .ExpectedArrival;

        return terminusArrival is not null && now >= terminusArrival.Value;
    }

    /// <summary>
    /// The server time the most recent uncorrected fact was recorded, or the journey's creation time
    /// when no fact has been recorded (M-02).
    /// </summary>
    private static DateTimeOffset LatestRecordedAt(
        IEnumerable<DepartureFact> departures,
        IEnumerable<ChangeFact> changes,
        DateTimeOffset createdAt)
    {
        var latest = createdAt;

        foreach (var departure in departures)
        {
            if (departure.RecordedAt > latest)
            {
                latest = departure.RecordedAt;
            }
        }

        foreach (var change in changes)
        {
            if (change.PublishedAt > latest)
            {
                latest = change.PublishedAt;
            }
        }

        return latest;
    }
}
