namespace SmartRail.SharedKernel.Infrastructure.Persistence;

/// <summary>
/// One row of a module's <c>outbox_messages</c> (design D2): an event, written in the same
/// transaction as the change that caused it. <c>position</c> is the dispatch order and
/// <c>published_at</c> stays null until the transport has accepted the message.
/// </summary>
internal sealed class OutboxMessage
{
    private OutboxMessage()
    {
    }

    internal OutboxMessage(
        Guid id,
        string eventType,
        string? orderingKey,
        string payload,
        DateTimeOffset occurredAt)
    {
        Id = id;
        EventType = eventType;
        OrderingKey = orderingKey;
        Payload = payload;
        OccurredAt = occurredAt;
    }

    public Guid Id { get; private set; }

    public long Position { get; private set; }

    public string EventType { get; private set; } = null!;

    public string? OrderingKey { get; private set; }

    public string Payload { get; private set; } = null!;

    public DateTimeOffset OccurredAt { get; private set; }

    public DateTimeOffset? PublishedAt { get; internal set; }
}
