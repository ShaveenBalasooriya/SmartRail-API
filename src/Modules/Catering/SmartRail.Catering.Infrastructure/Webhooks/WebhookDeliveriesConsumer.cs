using SmartRail.Catering.Application.Webhooks;
using SmartRail.LiveOperations.Contracts;
using SmartRail.SharedKernel.Application.Events;

namespace SmartRail.Catering.Infrastructure.Webhooks;

/// <summary>
/// Creates the provider's delivery records from the event bus (design D1). For one of the five
/// pushed event types it reads the event and its resulting live state through
/// <see cref="ILiveJourneys"/>, builds the payload and inserts one pending delivery for every
/// subscription that was already active when the event was recorded. Other event types are ignored,
/// which is what keeps departures and completed journeys off the provider's feed (webhook-delivery
/// spec, "Pushed event types").
/// </summary>
/// <remarks>
/// Delivery is at least once, so the unique <c>(subscription_id, event_id)</c> index and
/// <c>ON CONFLICT DO NOTHING</c> make a redelivered message harmless. The consumer is stateless:
/// the bus holds the delivery state, and a subscription's <c>created_at</c> is what "events from
/// registration onward" means (design D1).
/// </remarks>
internal sealed class WebhookDeliveriesConsumer(
    ILiveJourneys liveJourneys,
    WebhookPayloadFactory payloadFactory,
    IWebhookSubscriptionRepository subscriptions,
    IWebhookDeliveryRepository deliveries,
    TimeProvider timeProvider) : IEventConsumer<JourneyStateChanged>
{
    /// <inheritdoc />
    public string Name => "catering.webhook-deliveries";

    /// <inheritdoc />
    public async Task Handle(JourneyStateChanged integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        if (!WebhookEventTypes.TryMap(integrationEvent.EventType, out var eventType))
        {
            return;
        }

        // The message names the event and its position; the stream read supplies the snapshot the
        // payload is built from.
        var events = await liveJourneys.GetEventsAfterAsync(
            integrationEvent.StreamPosition - 1,
            integrationEvent.JourneyId,
            limit: 1,
            cancellationToken);

        var journeyEvent = events.FirstOrDefault(candidate => candidate.Id == integrationEvent.EventId);

        // A journey event the live state no longer holds cannot be turned into a payload; leaving it
        // unwritten lets a later redelivery still create the records if the state appears.
        if (journeyEvent is null)
        {
            return;
        }

        var subscriptionIds = await subscriptions.GetActiveCreatedAtOrBeforeAsync(
            journeyEvent.RecordedAt,
            cancellationToken);

        if (subscriptionIds.Count == 0)
        {
            return;
        }

        var payload = await payloadFactory.BuildAsync(eventType, journeyEvent, cancellationToken);

        await deliveries.AddForSubscriptionsAsync(
            subscriptionIds,
            journeyEvent,
            eventType,
            payload,
            timeProvider.GetUtcNow(),
            cancellationToken);
    }
}
