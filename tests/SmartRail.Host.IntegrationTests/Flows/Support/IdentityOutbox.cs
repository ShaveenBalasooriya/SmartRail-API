using SmartRail.Host.IntegrationTests.Infrastructure;

namespace SmartRail.Host.IntegrationTests.Flows.Support;

/// <summary>One announcement Identity published, with the state of its delivery to the audit consumer.</summary>
/// <param name="Id">The announcement's identifier, which is also its outbox row.</param>
/// <param name="Delivery">The delivery state, or <c>none</c> before the dispatcher has offered it.</param>
internal sealed record PublishedDenial(Guid Id, string Delivery);

/// <summary>
/// Identity's own outbox, which is where a refused write is announced. The flow uses it for two
/// things: to name the announcement a particular refusal produced (so its audit entry can be found
/// without guessing by time or by a global count), and to remove the announcements it caused, because
/// the shared database is left as the next test class expects to find it.
/// </summary>
internal sealed class IdentityOutbox
{
    /// <summary>The consumer that records a refusal in the audit record.</summary>
    private const string AuditConsumer = "audit.access-denied";

    /// <summary>How long cleanup gives a delivery to finish before it removes the row anyway.</summary>
    private static readonly TimeSpan CleanUpWindow = TimeSpan.FromSeconds(10);

    private readonly PostgresFixture _postgres;

    public IdentityOutbox(PostgresFixture postgres) => _postgres = postgres;

    /// <summary>The outbox's current highest position, which is where a flow's own window starts.</summary>
    public async Task<long> MaxPositionAsync() => Assert.Single(await _postgres.QueryAsync(
        "SELECT coalesce(max(position), 0) FROM identity.outbox_messages",
        reader => reader.GetInt64(0)));

    /// <summary>Waits for the one refusal published after <paramref name="baselinePosition"/>.</summary>
    public async Task<Guid> WaitForSingleDenialAsync(long baselinePosition, string behaviour)
    {
        var ids = await Eventually.ValueAsync(
            () => DenialIdsAfterAsync(baselinePosition),
            found => found.Count == 1,
            $"{behaviour} should be announced once through Identity's outbox");

        return ids[0];
    }

    /// <summary>Waits for the expected number of refusals published after the baseline.</summary>
    public async Task<IReadOnlyList<Guid>> WaitForDenialsAsync(
        long baselinePosition,
        int expected,
        string behaviour)
    {
        return await Eventually.ValueAsync(
            () => DenialIdsAfterAsync(baselinePosition),
            found => found.Count == expected,
            $"{behaviour} should announce {expected} refusals through Identity's outbox");
    }

    /// <summary>
    /// Waits until every refusal published after <paramref name="baselinePosition"/> has been delivered
    /// to the audit consumer. A flow uses this to know the refusal was fully processed before it reads
    /// the state the refusal was supposed to leave alone.
    /// </summary>
    public async Task WaitUntilDeliveredAsync(long baselinePosition, string behaviour)
    {
        await Eventually.ValueAsync(
            () => DeliveriesAsync(baselinePosition),
            deliveries => deliveries.Count > 0
                && deliveries.All(delivery => string.Equals(delivery.Delivery, "delivered", StringComparison.Ordinal)),
            behaviour);
    }

    /// <summary>
    /// Removes the announcements this flow caused and their deliveries. The flow waits briefly for a
    /// delivery still in flight so cleanup cannot race the consumer, and then removes the rows whatever
    /// the dispatcher did.
    /// </summary>
    public async Task CleanUpAfterAsync(long baselinePosition)
    {
        var deadline = DateTimeOffset.UtcNow.Add(CleanUpWindow);

        while (DateTimeOffset.UtcNow < deadline)
        {
            var deliveries = await DeliveriesAsync(baselinePosition);

            if (deliveries.Count > 0
                && deliveries.All(delivery => string.Equals(delivery.Delivery, "delivered", StringComparison.Ordinal)))
            {
                break;
            }

            await Task.Delay(Eventually.Step);
        }

        await _postgres.ExecuteAsync(
            $"""
            DELETE FROM identity.outbox_deliveries
            WHERE message_id IN (
                SELECT id FROM identity.outbox_messages WHERE position > {baselinePosition})
            """);

        await _postgres.ExecuteAsync(
            $"DELETE FROM identity.outbox_messages WHERE position > {baselinePosition}");
    }

    private async Task<IReadOnlyList<Guid>> DenialIdsAfterAsync(long baselinePosition) =>
        await _postgres.QueryAsync(
            $"""
            SELECT id
            FROM identity.outbox_messages
            WHERE event_type = 'AccessDenied' AND position > {baselinePosition}
            ORDER BY position
            """,
            reader => reader.GetGuid(0));

    private async Task<IReadOnlyList<PublishedDenial>> DeliveriesAsync(long baselinePosition) =>
        await _postgres.QueryAsync(
            $"""
            SELECT m.id, coalesce(d.status, 'none')
            FROM identity.outbox_messages m
            LEFT JOIN identity.outbox_deliveries d
                ON d.message_id = m.id AND d.consumer = '{AuditConsumer}'
            WHERE m.event_type = 'AccessDenied' AND m.position > {baselinePosition}
            ORDER BY m.position
            """,
            reader => new PublishedDenial(reader.GetGuid(0), reader.GetString(1)));
}
