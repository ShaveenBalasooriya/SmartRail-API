using SmartRail.SharedKernel.Primitives;

namespace SmartRail.LiveOperations.Domain.LiveState;

/// <summary>
/// One event of the journey event stream, append-only. <see cref="StreamPosition"/> is assigned by
/// the database from the module's sequence; <see cref="SequenceNumber"/> is the aggregate's
/// per-journey number. <see cref="Payload"/> is the journey's live-state snapshot as JSONB.
/// </summary>
public sealed class JourneyEvent : Entity<Guid>
{
    private JourneyEvent()
    {
    }

    private JourneyEvent(
        Guid id,
        Guid journeyId,
        long sequenceNumber,
        JourneyEventType eventType,
        Guid? statusChangeId,
        Guid? departureLogId,
        string payload,
        DateTimeOffset occurredAt,
        DateTimeOffset recordedAt)
        : base(id)
    {
        JourneyId = journeyId;
        SequenceNumber = sequenceNumber;
        EventType = eventType;
        StatusChangeId = statusChangeId;
        DepartureLogId = departureLogId;
        Payload = payload;
        OccurredAt = occurredAt;
        RecordedAt = recordedAt;
    }

    public Guid JourneyId { get; private set; }

    /// <summary>The per-journey number: 1, 2, 3, with no gaps and no repeats.</summary>
    public long SequenceNumber { get; private set; }

    /// <summary>The global position, assigned by the database sequence on insert.</summary>
    public long StreamPosition { get; private set; }

    public JourneyEventType EventType { get; private set; }

    /// <summary>The Controller change that produced the event, when it was a published change.</summary>
    public Guid? StatusChangeId { get; private set; }

    /// <summary>The Guard departure log that produced the event, when it was a departure.</summary>
    public Guid? DepartureLogId { get; private set; }

    /// <summary>The resulting live state, serialised as JSONB.</summary>
    public string Payload { get; private set; } = null!;

    /// <summary>When the fact occurred (for example, the device time of a departure).</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>When the server recorded the fact.</summary>
    public DateTimeOffset RecordedAt { get; private set; }

    /// <summary>Creates the event that records one committed change. Only the aggregate calls this.</summary>
    internal static JourneyEvent Create(
        Guid journeyId,
        long sequenceNumber,
        JourneyEventType eventType,
        Guid? sourceId,
        string payload,
        DateTimeOffset occurredAt,
        DateTimeOffset recordedAt)
    {
        var isDeparture = eventType == JourneyEventType.DepartureLogged;

        return new JourneyEvent(
            Guid.CreateVersion7(),
            journeyId,
            sequenceNumber,
            eventType,
            isDeparture ? null : sourceId,
            isDeparture ? sourceId : null,
            payload,
            occurredAt,
            recordedAt);
    }
}
