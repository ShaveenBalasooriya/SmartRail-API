namespace SmartRail.SharedKernel.Application.Events;

/// <summary>
/// Hands a recorded message to every consumer of its type (design D3). MVP 1's implementation is
/// in-process on PostgreSQL; a NATS JetStream implementation replaces it without touching a
/// publisher or a consumer.
/// </summary>
public interface IEventTransport
{
    /// <summary>
    /// Publishes <paramref name="message"/> to every consumer registered for its type, giving each
    /// one its own delivery. Publishing the same message twice must be harmless. An event with no
    /// registered consumer is published and gets no delivery.
    /// </summary>
    Task Publish(IntegrationEventMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delivers the rows this transport has already accepted and that are due now (design D5). It is
    /// part of the in-process transport because the transport owns per-consumer state; a broker
    /// transport redelivers by itself and implements this as a no-op.
    /// </summary>
    Task DeliverPending(CancellationToken cancellationToken = default);
}
