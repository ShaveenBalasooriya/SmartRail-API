using SmartRail.Catering.Domain.Webhooks;
using SmartRail.LiveOperations.Contracts;

namespace SmartRail.Catering.Application.Webhooks;

/// <summary>
/// The delivery record store. Records are written by Catering's consumer of the event bus, one per
/// active subscription, before anything is sent (webhook-delivery spec, "Persisted before sending";
/// design D1).
/// </summary>
public interface IWebhookDeliveryRepository
{
    /// <summary>
    /// Inserts one pending delivery per subscription for <paramref name="journeyEvent"/>, ignoring a
    /// subscription that already holds a delivery for that event, so a redelivered bus message
    /// writes nothing (webhook-delivery spec, "One delivery per event per subscription").
    /// </summary>
    /// <returns>The number of delivery rows inserted.</returns>
    Task<int> AddForSubscriptionsAsync(
        IReadOnlyList<Guid> subscriptionIds,
        JourneyEventView journeyEvent,
        WebhookEventType eventType,
        string payload,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}
