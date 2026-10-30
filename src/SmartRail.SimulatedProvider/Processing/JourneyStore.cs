namespace SmartRail.SimulatedProvider.Processing;

/// <summary>
/// The simulated provider's whole world: the journeys its kitchen serves, the events it has received
/// and the registration it made. All of it is in memory, because the simulated provider is a separate
/// system with no SmartRail database (simulated-provider spec, "Separate process"; design D6).
/// </summary>
/// <remarks>
/// Applying an event follows the ordering rules the provider API promises: an event ID already seen
/// is ignored, an event whose sequence number is not higher than the journey's last applied one is
/// recorded but not applied, and a correction recomputes the journey from the remaining applied
/// events, which is how a wrong cancellation is undone (CAT-06, CAT-07, CAT-09).
/// </remarks>
public sealed class JourneyStore
{
    private readonly object gate = new();
    private readonly Guid stationId;
    private readonly Dictionary<Guid, JourneyState> baselines = [];
    private readonly Dictionary<Guid, JourneyState> journeys = [];
    private readonly Dictionary<Guid, List<AppliedEvent>> appliedEvents = [];
    private readonly HashSet<Guid> processedEventIds = [];
    private readonly List<ReceivedEvent> receivedEvents = [];

    private RegistrationSnapshot registration = new(false, null, null, "not attempted yet");
    private string? stationName;
    private DateTimeOffset? loadedAt;
    private bool failing;

    /// <summary>Creates the store for the station whose journeys the provider serves.</summary>
    public JourneyStore(Guid stationId) => this.stationId = stationId;

    /// <summary>Whether the failure switch is on, so the webhook endpoint answers 503.</summary>
    public bool IsFailing => failing;

    /// <summary>Turns the failure switch on or off (simulated-provider spec, "Failure injection").</summary>
    public bool ToggleFailure()
    {
        lock (gate)
        {
            failing = !failing;

            return failing;
        }
    }

    /// <summary>Records the subscription this provider registered.</summary>
    public void RecordRegistration(Guid? subscriptionId, string url, string? error)
    {
        lock (gate)
        {
            registration = new RegistrationSnapshot(error is null, subscriptionId, url, error);
        }
    }

    /// <summary>
    /// Replaces the loaded journeys with the provider API's current answer for this provider's
    /// station. It runs on start-up, before any event is applied.
    /// </summary>
    public void LoadJourneys(
        ProviderStation? station,
        IReadOnlyList<ProviderJourney> loadedJourneys,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(loadedJourneys);

        lock (gate)
        {
            baselines.Clear();
            journeys.Clear();
            appliedEvents.Clear();

            foreach (var journey in loadedJourneys)
            {
                baselines[journey.JourneyId] = JourneyState.FromProvider(journey);
                journeys[journey.JourneyId] = JourneyState.FromProvider(journey);
            }

            if (station is not null)
            {
                stationName = station.Name;
            }

            loadedAt = now;
        }
    }

    /// <summary>
    /// Applies one pushed event and returns the outcome the event log shows:
    /// <c>applied</c>, <c>ignored_duplicate</c>, <c>out_of_order</c> or <c>ignored_unknown_journey</c>.
    /// </summary>
    public string Apply(ProviderWebhookEvent payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        lock (gate)
        {
            if (!processedEventIds.Add(payload.EventId))
            {
                return Record(payload, "ignored_duplicate");
            }

            if (!journeys.TryGetValue(payload.JourneyId, out var current))
            {
                return Record(payload, "ignored_unknown_journey");
            }

            var applied = AppliedEventsOf(payload.JourneyId);

            if (payload.SequenceNumber <= current.LastAppliedSequence)
            {
                applied.Add(new AppliedEvent(payload, isApplied: false));
                return Record(payload, "out_of_order");
            }

            if (payload.Type == "correction_published" && payload.CorrectsEventId is { } correctedEventId)
            {
                foreach (var candidate in applied)
                {
                    if (candidate.Event.EventId == correctedEventId)
                    {
                        candidate.Corrected = true;
                    }
                }
            }

            applied.Add(new AppliedEvent(payload, isApplied: true));
            Recompute(payload.JourneyId, applied);

            return Record(payload, "applied");
        }
    }

    /// <summary>The provider's view of its world, as <c>GET /state</c> returns it.</summary>
    public ProviderStateSnapshot Snapshot()
    {
        lock (gate)
        {
            var snapshotJourneys = journeys.Values
                .OrderBy(journey => journey.ExpectedArrival
                    ?? journey.ScheduledArrival
                    ?? DateTimeOffset.MaxValue)
                .ThenBy(journey => journey.JourneyId)
                .Select(journey => journey.ToSnapshot())
                .ToList();

            return new ProviderStateSnapshot(
                new StationSnapshot(stationId, stationName),
                registration,
                loadedAt,
                failing,
                snapshotJourneys,
                [.. receivedEvents]);
        }
    }

    private List<AppliedEvent> AppliedEventsOf(Guid journeyId)
    {
        if (!appliedEvents.TryGetValue(journeyId, out var events))
        {
            events = [];
            appliedEvents[journeyId] = events;
        }

        return events;
    }

    /// <summary>
    /// Rebuilds a journey from its provider baseline and its applied events in sequence order, which
    /// is what makes a correction restore the state remaining after it (simulated-provider spec,
    /// "Cancellation and correction").
    /// </summary>
    private void Recompute(Guid journeyId, List<AppliedEvent> applied)
    {
        var recomputed = baselines[journeyId].Copy();
        var lastAppliedSequence = baselines[journeyId].LastAppliedSequence;

        foreach (var candidate in applied
                     .Where(candidate => candidate.IsApplied && !candidate.Corrected)
                     .OrderBy(candidate => candidate.Event.SequenceNumber))
        {
            ApplyEvent(recomputed, candidate.Event);
            lastAppliedSequence = Math.Max(lastAppliedSequence, candidate.Event.SequenceNumber);
        }

        recomputed.LastAppliedSequence = lastAppliedSequence;
        journeys[journeyId] = recomputed;
    }

    /// <summary>Applies one event to a journey's state. A correction has no direct effect.</summary>
    private void ApplyEvent(JourneyState journey, ProviderWebhookEvent payload)
    {
        switch (payload.Type)
        {
            case "delay_published":
                if (payload.DelayMinutes is { } delayMinutes)
                {
                    journey.DelayMinutes = delayMinutes;
                    journey.ExpectedArrival = journey.ScheduledArrival?.AddMinutes(delayMinutes);
                }

                break;

            case "cancellation_published":
                journey.Status = "cancelled";
                journey.ExpectedArrival = null;
                break;

            case "termination_published":
                journey.Status = "terminated";

                // A train ending at this provider's own station still arrives here; one ending
                // earlier no longer reaches it (simulated-provider spec, "Early termination").
                if (payload.StationId != stationId)
                {
                    journey.ExpectedArrival = null;
                }

                break;

            case "platform_changed":
                journey.Platform = payload.PlatformNumber;
                journey.PlatformChangedAt = payload.PublishedAt;
                break;

            case "correction_published":
            default:
                break;
        }
    }

    private string Record(ProviderWebhookEvent payload, string outcome)
    {
        receivedEvents.Add(new ReceivedEvent(
            payload.EventId,
            payload.JourneyId,
            payload.Type,
            payload.SequenceNumber,
            DateTimeOffset.UtcNow,
            outcome));

        return outcome;
    }

    /// <summary>One event the provider was told about, with whether it changed the state.</summary>
    private sealed class AppliedEvent(ProviderWebhookEvent @event, bool isApplied)
    {
        public ProviderWebhookEvent Event { get; } = @event;

        public bool IsApplied { get; } = isApplied;

        public bool Corrected { get; set; }
    }

    /// <summary>A journey's mutable live state, rebuilt from its baseline whenever a correction arrives.</summary>
    private sealed class JourneyState
    {
        public required Guid JourneyId { get; init; }

        public string? Line { get; init; }

        public string? Direction { get; init; }

        public Guid? FinalDestinationId { get; init; }

        public string? FinalDestinationName { get; init; }

        public DateTimeOffset? ScheduledArrival { get; init; }

        public DateTimeOffset? ExpectedArrival { get; set; }

        public string? Platform { get; set; }

        public string? Status { get; set; }

        public long LastAppliedSequence { get; set; }

        public int? DelayMinutes { get; set; }

        public DateTimeOffset? PlatformChangedAt { get; set; }

        public static JourneyState FromProvider(ProviderJourney journey) => new()
        {
            JourneyId = journey.JourneyId,
            Line = journey.Line,
            Direction = journey.Direction,
            FinalDestinationId = journey.FinalDestinationId,
            FinalDestinationName = journey.FinalDestinationName,
            ScheduledArrival = journey.ScheduledArrival,
            ExpectedArrival = journey.ExpectedArrival,
            Platform = journey.Platform,
            Status = journey.Status,
            LastAppliedSequence = journey.SequenceNumber,
        };

        public JourneyState Copy() => new()
        {
            JourneyId = JourneyId,
            Line = Line,
            Direction = Direction,
            FinalDestinationId = FinalDestinationId,
            FinalDestinationName = FinalDestinationName,
            ScheduledArrival = ScheduledArrival,
            ExpectedArrival = ExpectedArrival,
            Platform = Platform,
            Status = Status,
            LastAppliedSequence = LastAppliedSequence,
            DelayMinutes = DelayMinutes,
            PlatformChangedAt = PlatformChangedAt,
        };

        public JourneySnapshot ToSnapshot() => new(
            JourneyId,
            Line,
            Direction,
            FinalDestinationId,
            FinalDestinationName,
            ScheduledArrival,
            ExpectedArrival,
            Platform,
            Status ?? "scheduled",
            LastAppliedSequence,
            DelayMinutes);
    }
}
