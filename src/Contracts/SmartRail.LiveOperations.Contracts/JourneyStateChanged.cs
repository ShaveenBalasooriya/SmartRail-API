namespace SmartRail.LiveOperations.Contracts;

/// <summary>
/// Published when a fact changes a journey's live state: its outbox row is saved in the same
/// transaction that appends the journey event and delivered to every registered consumer once that
/// transaction commits (design D1, D9). It carries only the identifiers and ordering numbers: a
/// consumer reads the event's snapshot through <see cref="ILiveJourneys"/> when it needs the
/// resulting live state, so the message stays small and never carries a stale copy.
/// </summary>
public sealed record JourneyStateChanged(
    Guid JourneyId,
    Guid EventId,
    long SequenceNumber,
    long StreamPosition,
    JourneyEventType EventType);
