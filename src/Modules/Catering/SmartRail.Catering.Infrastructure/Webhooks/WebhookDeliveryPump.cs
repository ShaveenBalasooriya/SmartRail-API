using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;
using SmartRail.Catering.Application.Webhooks;
using SmartRail.Catering.Infrastructure.Persistence;

namespace SmartRail.Catering.Infrastructure.Webhooks;

/// <summary>
/// The webhook dispatcher's work (design D2; webhook-delivery spec). It abandons deliveries whose
/// subscription is no longer active, then, one row at a time, locks the oldest due delivery of each
/// journey's queue with <c>SELECT ... FOR UPDATE SKIP LOCKED</c>, posts it and records the attempt.
/// Only the lowest-sequence delivery of a subscription and journey that is neither delivered nor
/// abandoned is eligible, so a later event waits while an earlier one is retried.
/// </summary>
/// <remarks>
/// Each claim runs in its own short transaction: the row lock is held for the one POST, never for a
/// whole batch, so a slow receiver cannot hold other journeys' rows. The SQL lives here because raw
/// SQL is allowed only inside a repository method, and the module's Infrastructure is where its
/// repositories are.
/// </remarks>
internal sealed class WebhookDeliveryPump(
    CateringDbContext database,
    IWebhookSender sender,
    WebhookDispatcherOptions options,
    TimeProvider timeProvider,
    ILogger<WebhookDeliveryPump> logger) : IWebhookDeliveryPump
{
    private const string DueDeliverySql =
        """
        SELECT d.id, d.subscription_id, d.event_id, d.journey_id, d.sequence_number, d.payload,
               d.attempt_count, d.created_at, s.callback_url, s.is_active
        FROM catering.webhook_deliveries d
        JOIN catering.webhook_subscriptions s ON s.id = d.subscription_id
        WHERE d.status IN ('pending', 'failed')
          AND d.next_attempt_at <= @now
          AND d.sequence_number = (
              SELECT min(earlier.sequence_number)
              FROM catering.webhook_deliveries earlier
              WHERE earlier.subscription_id = d.subscription_id
                AND earlier.journey_id = d.journey_id
                AND earlier.status IN ('pending', 'failed'))
        ORDER BY d.next_attempt_at, d.id
        LIMIT 1
        FOR UPDATE OF d SKIP LOCKED
        """;

    public async Task<int> PumpAsync(CancellationToken cancellationToken = default)
    {
        await AbandonDeliveriesOfInactiveSubscriptionsAsync(cancellationToken);

        var attempted = 0;

        for (var pass = 0; pass < options.BatchSize; pass++)
        {
            var outcome = await AttemptOneAsync(cancellationToken);

            if (outcome == ClaimOutcome.None)
            {
                break;
            }

            if (outcome == ClaimOutcome.Attempted)
            {
                attempted++;
            }
        }

        return attempted;
    }

    /// <summary>
    /// Abandons every pending or failed delivery whose subscription has been deactivated or
    /// replaced, without sending it (webhook-delivery spec, "Deactivated subscriptions").
    /// </summary>
    private async Task AbandonDeliveriesOfInactiveSubscriptionsAsync(CancellationToken cancellationToken)
    {
        var abandoned = await database.Database.ExecuteSqlRawAsync(
            """
            UPDATE catering.webhook_deliveries d
            SET status = 'abandoned'
            FROM catering.webhook_subscriptions s
            WHERE s.id = d.subscription_id
              AND NOT s.is_active
              AND d.status IN ('pending', 'failed')
            """,
            cancellationToken);

        if (abandoned > 0)
        {
            WebhookDeliveryMetrics.Abandoned.Add(abandoned);
        }
    }

    /// <summary>Claims at most one due delivery and handles it, in its own transaction.</summary>
    private async Task<ClaimOutcome> AttemptOneAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        var delivery = await ClaimAsync(cancellationToken);

        if (delivery is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return ClaimOutcome.None;
        }

        var now = timeProvider.GetUtcNow();

        if (!delivery.IsActive)
        {
            await SetAbandonedAsync(delivery.Id, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            WebhookDeliveryMetrics.Abandoned.Add(1);
            return ClaimOutcome.Abandoned;
        }

        var result = await sender.SendAsync(delivery.CallbackUrl, delivery.Payload, cancellationToken);

        await RecordAttemptAsync(delivery, result, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        RecordMetrics(delivery, result, now);
        LogFailure(delivery, result);

        return ClaimOutcome.Attempted;
    }

    /// <summary>Locks and reads the next due delivery, or returns null when none is due.</summary>
    private async Task<DueDelivery?> ClaimAsync(CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)database.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.Transaction = (NpgsqlTransaction?)database.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = DueDeliverySql;
        command.Parameters.AddWithValue("now", timeProvider.GetUtcNow());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new DueDelivery(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetGuid(3),
            reader.GetInt64(4),
            reader.GetString(5),
            reader.GetInt32(6),
            reader.GetFieldValue<DateTimeOffset>(7),
            reader.GetString(8),
            reader.GetBoolean(9));
    }

    private Task<int> SetAbandonedAsync(Guid deliveryId, CancellationToken cancellationToken) =>
        database.Database.ExecuteSqlRawAsync(
            "UPDATE catering.webhook_deliveries SET status = 'abandoned' WHERE id = {0}",
            [deliveryId],
            cancellationToken);

    private Task<int> RecordAttemptAsync(
        DueDelivery delivery,
        WebhookSendResult result,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var attempts = delivery.AttemptCount + 1;

        if (result.Delivered)
        {
            return database.Database.ExecuteSqlRawAsync(
                """
                UPDATE catering.webhook_deliveries
                SET status = 'delivered', attempt_count = {1}, last_attempt_at = {2},
                    last_response_status = {3}, delivered_at = {2}, next_attempt_at = {2}
                WHERE id = {0}
                """,
                [delivery.Id, attempts, now, result.StatusCode ?? (object)DBNull.Value],
                cancellationToken);
        }

        var abandoned = WebhookDispatcherOptions.IsAbandoned(attempts);
        var nextAttemptAt = abandoned ? now : now + options.RetryDelay(attempts);

        return database.Database.ExecuteSqlRawAsync(
            """
            UPDATE catering.webhook_deliveries
            SET status = {1}, attempt_count = {2}, last_attempt_at = {3},
                last_response_status = {4}, next_attempt_at = {5}
            WHERE id = {0}
            """,
            [
                delivery.Id,
                abandoned ? "abandoned" : "failed",
                attempts,
                now,
                result.StatusCode ?? (object)DBNull.Value,
                nextAttemptAt,
            ],
            cancellationToken);
    }

    /// <summary>
    /// Counts the attempt and its outcome. Only the event type is a tag, so no tag can carry a
    /// payload or a callback URL (design D7, S-19).
    /// </summary>
    private static void RecordMetrics(DueDelivery delivery, WebhookSendResult result, DateTimeOffset now)
    {
        // The event type is not on the row read; the payload names it, and the metric's contract
        // does not need it, so the counters carry no per-delivery tag that could hold provider data.
        WebhookDeliveryMetrics.Attempts.Add(1);

        if (!result.Delivered)
        {
            WebhookDeliveryMetrics.Failures.Add(1);

            if (WebhookDispatcherOptions.IsAbandoned(delivery.AttemptCount + 1))
            {
                WebhookDeliveryMetrics.Abandoned.Add(1);
            }

            return;
        }

        WebhookDeliveryMetrics.DeliveryLatency.Record((now - delivery.CreatedAt).TotalSeconds);
    }

    /// <summary>
    /// Logs a failed attempt with its identifiers and status only. The payload and the callback URL are
    /// never logged, so a query string cannot leak (webhook-delivery spec, "Failure visibility").
    /// </summary>
    private void LogFailure(DueDelivery delivery, WebhookSendResult result)
    {
        if (result.Delivered)
        {
            return;
        }

        logger.LogWarning(
            "Webhook delivery of event {EventId} to subscription {SubscriptionId} failed on attempt {Attempt}: {StatusCode}",
            delivery.EventId,
            delivery.SubscriptionId,
            delivery.AttemptCount + 1,
            result.StatusCode?.ToString(CultureInfo.InvariantCulture) ?? "no response");
    }

    /// <summary>One claimed delivery, with the subscription data needed to post it.</summary>
    private sealed record DueDelivery(
        Guid Id,
        Guid SubscriptionId,
        Guid EventId,
        Guid JourneyId,
        long SequenceNumber,
        string Payload,
        int AttemptCount,
        DateTimeOffset CreatedAt,
        string CallbackUrl,
        bool IsActive);

    /// <summary>What one claim did, so a pass knows whether to continue.</summary>
    private enum ClaimOutcome
    {
        /// <summary>Nothing was due.</summary>
        None,

        /// <summary>A delivery was posted and recorded.</summary>
        Attempted,

        /// <summary>A delivery was abandoned because its subscription is inactive.</summary>
        Abandoned,
    }
}
