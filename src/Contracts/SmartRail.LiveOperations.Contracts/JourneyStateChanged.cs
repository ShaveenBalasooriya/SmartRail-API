using MediatR;

namespace SmartRail.LiveOperations.Contracts;

/// <summary>
/// Announced after the transaction that appended a journey event commits (design D5). It carries
/// only the identifiers and ordering numbers: a subscriber reads the event's snapshot through
/// <see cref="ILiveJourneys"/> when it needs the resulting live state, so the notification stays
/// small and never carries a stale copy.
/// </summary>
public sealed record JourneyStateChanged(
    Guid JourneyId,
    Guid EventId,
    long SequenceNumber,
    long StreamPosition,
    JourneyEventType EventType) : INotification;
