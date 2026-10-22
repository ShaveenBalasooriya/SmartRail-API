namespace SmartRail.SharedKernel.Infrastructure.Persistence;

/// <summary>
/// One row of a module's <c>outbox_deliveries</c> (design D5): the delivery state of one message to
/// one consumer. Each consumer's state is its own row, so one failing consumer neither blocks nor
/// repeats delivery to another.
/// </summary>
internal sealed class OutboxDelivery
{
    private OutboxDelivery()
    {
    }

    internal OutboxDelivery(Guid messageId, string consumer)
    {
        MessageId = messageId;
        Consumer = consumer;
        Status = OutboxDeliveryStatus.Pending;
    }

    public Guid MessageId { get; private set; }

    public string Consumer { get; private set; } = null!;

    public OutboxDeliveryStatus Status { get; internal set; }

    public int AttemptCount { get; internal set; }

    public DateTimeOffset? NextAttemptAt { get; internal set; }

    public DateTimeOffset? LastAttemptAt { get; internal set; }

    public DateTimeOffset? DeliveredAt { get; internal set; }
}

/// <summary>The three states of a delivery (design D5).</summary>
internal enum OutboxDeliveryStatus
{
    Pending,
    Delivered,
    Failed,
}
