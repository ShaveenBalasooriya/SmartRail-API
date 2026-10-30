using Microsoft.EntityFrameworkCore;
using SmartRail.Catering.Application.Webhooks;
using SmartRail.Catering.Domain.Webhooks;
using SmartRail.Catering.Infrastructure.Persistence;
using SmartRail.LiveOperations.Contracts;

namespace SmartRail.Catering.Infrastructure.Webhooks;

/// <summary>
/// Writes <c>catering.webhook_deliveries</c>. The insert is raw so it can carry
/// <c>ON CONFLICT (subscription_id, event_id) DO NOTHING</c>: the bus delivers at least once, and a
/// redelivery must leave the one row the first delivery created (webhook-delivery spec, "One
/// delivery per event per subscription"; design D1).
/// </summary>
internal sealed class WebhookDeliveryRepository(CateringDbContext database)
    : IWebhookDeliveryRepository
{
    public async Task<int> AddForSubscriptionsAsync(
        IReadOnlyList<Guid> subscriptionIds,
        JourneyEventView journeyEvent,
        WebhookEventType eventType,
        string payload,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriptionIds);
        ArgumentNullException.ThrowIfNull(journeyEvent);

        var inserted = 0;
        var eventTypeValue = WebhookEventTypes.ToStorageValue(eventType);

        foreach (var subscriptionId in subscriptionIds)
        {
            inserted += await database.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO catering.webhook_deliveries
                    (id, subscription_id, event_id, journey_id, sequence_number, event_type, payload,
                     status, attempt_count, next_attempt_at, created_at)
                VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}::jsonb, 'pending', 0, {7}, {8})
                ON CONFLICT (subscription_id, event_id) DO NOTHING
                """,
                [
                    Guid.CreateVersion7(),
                    subscriptionId,
                    journeyEvent.Id,
                    journeyEvent.JourneyId,
                    journeyEvent.SequenceNumber,
                    eventTypeValue,
                    payload,
                    now,
                    now,
                ],
                cancellationToken);
        }

        return inserted;
    }
}
