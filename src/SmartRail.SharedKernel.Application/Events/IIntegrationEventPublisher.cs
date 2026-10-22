namespace SmartRail.SharedKernel.Application.Events;

/// <summary>
/// Publishes an event that other modules react to (design D1, D2). The implementation adds an
/// <c>outbox_messages</c> row through the publishing module's own <c>DbContext</c>, so the caller's
/// existing unit of work saves it in the same transaction as the change that caused it. It never
/// saves itself, and nothing is delivered before that transaction commits.
/// </summary>
public interface IIntegrationEventPublisher
{
    /// <summary>
    /// Records <paramref name="integrationEvent"/> as an outbox row. <paramref name="eventId"/> is the
    /// event's identity and stays the same across redelivery; <paramref name="orderingKey"/> orders
    /// delivery per consumer and key, and is the journey ID for journey events (design D6).
    /// </summary>
    void Publish(object integrationEvent, Guid eventId, string? orderingKey = null);
}
