using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartRail.SharedKernel.Application.Events;
using SmartRail.SharedKernel.Infrastructure.Persistence;

namespace SmartRail.SharedKernel.Infrastructure.Events;

/// <summary>
/// MVP 1's transport: in-process on PostgreSQL (design D3, D5, D6). Publishing creates one pending
/// <c>outbox_deliveries</c> row per registered consumer, in the publishing module's own schema;
/// delivery runs each consumer in a new scope, retries with backoff and marks a delivery
/// <c>failed</c> after its fifth failure. A NATS JetStream transport replaces this class, taking
/// per-consumer state, redelivery and replay with it.
/// </summary>
internal sealed class PostgresEventTransport(
    DbContext dbContext,
    string schema,
    EventConsumerRegistry consumers,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<PostgresEventTransport> logger) : IEventTransport
{
    /// <summary>The wait after the first, second, third and fourth failed attempt (design D5).</summary>
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
    ];

    /// <summary>A delivery is attempted five times in total before it is marked <c>failed</c>.</summary>
    private const int MaxAttempts = 5;

    public async Task Publish(IntegrationEventMessage message, CancellationToken cancellationToken = default)
    {
        if (!consumers.HasConsumers(message.EventType))
        {
            // An event nobody consumes is marked published and gets no delivery rows.
            return;
        }

        // Names come from the consumer instances, so they are read in a throwaway scope rather than
        // in the dispatcher's outbox scope.
        IReadOnlyList<string> names;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            names = [.. consumers.Resolve(message.EventType, scope.ServiceProvider)
                .Select(consumer => consumer.Name)
                .Distinct()];
        }

        foreach (var name in names)
        {
            // ON CONFLICT DO NOTHING makes a republish after a crash before published_at harmless
            // (design D3); a tracked insert would fail on the second attempt instead. The schema is
            // the module's own registration constant, never a caller value, and both values are
            // parameters, so EF1003's concatenation warning does not apply.
#pragma warning disable EF1003
            await dbContext.Database.ExecuteSqlRawAsync(
                $"INSERT INTO {schema}.outbox_deliveries (message_id, consumer, status, attempt_count) " +
                "VALUES ({0}, {1}, 'pending', 0) ON CONFLICT (message_id, consumer) DO NOTHING",
                [message.Id, name],
                cancellationToken);
#pragma warning restore EF1003
        }
    }

    public async Task DeliverPending(CancellationToken cancellationToken = default)
    {
        var pending = await dbContext.Set<OutboxDelivery>()
            .Where(delivery => delivery.Status == OutboxDeliveryStatus.Pending)
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
        {
            return;
        }

        var messageIds = pending.Select(delivery => delivery.MessageId).Distinct().ToList();
        var messages = await dbContext.Set<OutboxMessage>()
            .Where(message => messageIds.Contains(message.Id))
            .ToDictionaryAsync(message => message.Id, cancellationToken);

        // A delivery carries no foreign key to its message. A publisher that was already reading a
        // message when another writer removed it still records that message's deliveries, and a
        // delivery whose message is gone can never succeed. It is skipped rather than looked up, so
        // one removed message cannot fail every later cycle of this module's outbox.
        var deliverable = pending
            .Where(delivery => messages.ContainsKey(delivery.MessageId))
            .ToList();

        // Per consumer and ordering key, only the earliest pending row may run: a later message
        // waits while an earlier one for the same consumer and key is pending, even when that
        // earlier one is not due yet (design D6). A failed row is no longer pending, so it releases
        // its key. Messages without a key carry no ordering promise.
        var blocked = new HashSet<(string Consumer, string OrderingKey)>();

        var ordered = deliverable
            .Select(delivery => (Delivery: delivery, Message: messages[delivery.MessageId]))
            .OrderBy(item => item.Message.Position);

        foreach (var (delivery, message) in ordered)
        {
            if (message.OrderingKey is { } orderingKey && !blocked.Add((delivery.Consumer, orderingKey)))
            {
                continue;
            }

            var now = timeProvider.GetUtcNow();

            if (delivery.NextAttemptAt is { } nextAttemptAt && nextAttemptAt > now)
            {
                continue;
            }

            await DeliverAsync(delivery, message, now, cancellationToken);
        }
    }

    private async Task DeliverAsync(
        OutboxDelivery delivery,
        OutboxMessage message,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Attempts are counted including the one that succeeds, so a delivery that failed once and
        // then succeeded records two attempts (event-delivery spec: consumer recovers).
        delivery.AttemptCount++;
        delivery.LastAttemptAt = now;

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();

            var consumer = consumers.Resolve(message.EventType, scope.ServiceProvider)
                .FirstOrDefault(candidate => candidate.Name == delivery.Consumer)
                ?? throw new InvalidOperationException(
                    $"No consumer '{delivery.Consumer}' is registered for event type '{message.EventType}'.");

            await consumer.Handle(message.Payload, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // A failure counts against this consumer only; another consumer of the same message
            // has its own row and is unaffected (design D5).
            if (delivery.AttemptCount >= MaxAttempts)
            {
                delivery.Status = OutboxDeliveryStatus.Failed;
                delivery.NextAttemptAt = null;
            }
            else
            {
                delivery.NextAttemptAt = now + RetryDelays[delivery.AttemptCount - 1];
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            // Design D10: one counter increment per failed attempt, one more when the delivery has
            // just failed for good, and one log entry naming the event ID, consumer and attempt.
            // Only the event type and consumer name are tags, and only the exception's type is
            // logged, so neither a metric nor a log entry can carry the payload.
            var tags = new TagList
            {
                { EventDeliveryMetrics.EventTypeTag, message.EventType },
                { EventDeliveryMetrics.ConsumerTag, delivery.Consumer },
            };

            EventDeliveryMetrics.DeliveryFailures.Add(1, tags);

            if (delivery.Status == OutboxDeliveryStatus.Failed)
            {
                EventDeliveryMetrics.DeliveriesFailed.Add(1, tags);
            }

            logger.LogWarning(
                "Delivery of event {EventId} to {Consumer} failed on attempt {Attempt}: {ExceptionType}",
                message.Id,
                delivery.Consumer,
                delivery.AttemptCount,
                exception.GetType().Name);

            return;
        }

        delivery.Status = OutboxDeliveryStatus.Delivered;
        delivery.DeliveredAt = now;
        delivery.NextAttemptAt = null;
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
