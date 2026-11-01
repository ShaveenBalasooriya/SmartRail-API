using SmartRail.Host.IntegrationTests.Flows.Builders;
using SmartRail.Host.IntegrationTests.Infrastructure;

namespace SmartRail.Host.IntegrationTests.Flows.Support;

/// <summary>
/// The one place in the flows that speaks SQL, and the only thing it does is remove what a flow added.
/// The database is shared with every other integration class, so a flow that left its journeys, its
/// announcements or its audit entries behind would change what the next class reads.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here is an assertion: it runs once from the flow's teardown, and only in-process mode, where
/// the flow owns rows in the run's database. A remote run cleans nothing, because it wrote nothing
/// directly.
/// </para>
/// <para>
/// Two of the three deletes are scoped by a baseline read when the flow starts — the identity outbox's
/// newest position and the audit chain's highest position. These are baselines, never "the row I want":
/// every assertion the flows make finds its row through a public interface, and the baseline only says
/// where this flow's own rows begin.
/// </para>
/// </remarks>
internal sealed class FlowCleanup
{
    /// <summary>The seeded provider (<c>seed/catering/providers.json</c>), whose subscription a flow replaces.</summary>
    private static readonly Guid ProviderId = Guid.Parse("01a15874-e6a0-7546-9097-3dae27a35461");

    /// <summary>The consumer that records a refusal in the audit record.</summary>
    private const string AuditConsumer = "audit.access-denied";

    /// <summary>How long cleanup gives a delivery to finish before it removes the row anyway.</summary>
    private static readonly TimeSpan DeliveryWindow = TimeSpan.FromSeconds(10);

    private readonly PostgresFixture _postgres;
    private readonly long _identityPosition;
    private readonly long _chainPosition;

    private FlowCleanup(PostgresFixture postgres, long identityPosition, long chainPosition)
    {
        _postgres = postgres;
        _identityPosition = identityPosition;
        _chainPosition = chainPosition;
    }

    /// <summary>
    /// Reads where this flow's rows begin, so the deletes below can be scoped to them. Called once, as
    /// the flow starts: anything above either baseline belongs to this flow.
    /// </summary>
    public static async Task<FlowCleanup> CaptureAsync(PostgresFixture postgres)
    {
        ArgumentNullException.ThrowIfNull(postgres);

        var identity = Assert.Single(await postgres.QueryAsync(
            "SELECT coalesce(max(position), 0) FROM identity.outbox_messages",
            reader => reader.GetInt64(0)));

        var chain = Assert.Single(await postgres.QueryAsync(
            "SELECT coalesce(max(chain_position), 0) FROM audit.entries",
            reader => reader.GetInt64(0)));

        return new FlowCleanup(postgres, identity, chain);
    }

    /// <summary>
    /// Removes everything the flow created: its journeys with their logs, changes, events and
    /// announcements; the provider subscription it registered; the refusals it announced; and the audit
    /// entries it appended. The order matters — a change refers to the change it corrects, a delivery
    /// refers to its message, and an entry lands only after the delivery that produced it.
    /// </summary>
    /// <param name="journeys">The journeys the flow created.</param>
    /// <param name="providerSubscribed">True when the flow registered the provider's callback.</param>
    public async Task RunAsync(IReadOnlyList<Journey> journeys, bool providerSubscribed)
    {
        ArgumentNullException.ThrowIfNull(journeys);

        foreach (var journey in journeys)
        {
            await RemoveJourneyAsync(journey.Id);
        }

        if (providerSubscribed)
        {
            await RemoveSubscriptionAsync();
        }

        await RemoveAnnouncementsAsync();
        await RemoveAuditEntriesAsync();
    }

    /// <summary>Removes one journey and every row the Live Operations module stored for it.</summary>
    private async Task RemoveJourneyAsync(Guid journeyId)
    {
        await _postgres.ExecuteAsync(
            $"DELETE FROM operations.departure_logs WHERE journey_id = '{journeyId}'");

        await _postgres.ExecuteAsync(
            $"""
            DELETE FROM operations.status_changes
            WHERE journey_id = '{journeyId}' AND corrects_change_id IS NOT NULL
            """);

        await _postgres.ExecuteAsync(
            $"DELETE FROM operations.status_changes WHERE journey_id = '{journeyId}'");

        // The delivery rows refer to the message by identifier alone, so they go first: a pending row
        // whose message had been deleted would stall the module's outbox.
        await _postgres.ExecuteAsync(
            $"""
            DELETE FROM operations.outbox_deliveries
            WHERE message_id IN (
                SELECT id FROM operations.outbox_messages WHERE ordering_key = '{journeyId}')
            """);

        await _postgres.ExecuteAsync(
            $"DELETE FROM operations.outbox_messages WHERE ordering_key = '{journeyId}'");

        await _postgres.ExecuteAsync(
            $"DELETE FROM operations.journey_events WHERE journey_id = '{journeyId}'");

        await _postgres.ExecuteAsync(
            $"DELETE FROM operations.journey_stop_live_states WHERE journey_id = '{journeyId}'");

        await _postgres.ExecuteAsync(
            $"DELETE FROM operations.journey_live_states WHERE journey_id = '{journeyId}'");

        await _postgres.ExecuteAsync(
            $"""
            DELETE FROM network.outbox_deliveries
            WHERE message_id IN (
                SELECT id FROM network.outbox_messages
                WHERE id = '{journeyId}' OR ordering_key = '{journeyId}')
            """);

        await _postgres.ExecuteAsync(
            $"""
            DELETE FROM network.outbox_messages
            WHERE id = '{journeyId}' OR ordering_key = '{journeyId}'
            """);
    }

    /// <summary>Removes the provider's callback subscription and the deliveries it caused.</summary>
    private async Task RemoveSubscriptionAsync()
    {
        await _postgres.ExecuteAsync(
            $"""
            DELETE FROM catering.webhook_deliveries
            WHERE subscription_id IN (
                SELECT id FROM catering.webhook_subscriptions WHERE provider_id = '{ProviderId}')
            """);

        await _postgres.ExecuteAsync(
            $"DELETE FROM catering.webhook_subscriptions WHERE provider_id = '{ProviderId}'");
    }

    /// <summary>
    /// Removes the refusals the flow caused and their deliveries. It waits briefly for a delivery still
    /// in flight, so a delete cannot race the consumer that turns it into an audit entry.
    /// </summary>
    private async Task RemoveAnnouncementsAsync()
    {
        var deadline = DateTimeOffset.UtcNow.Add(DeliveryWindow);

        while (DateTimeOffset.UtcNow < deadline)
        {
            var deliveries = await _postgres.QueryAsync(
                $"""
                SELECT m.id, coalesce(d.status, 'none')
                FROM identity.outbox_messages m
                LEFT JOIN identity.outbox_deliveries d
                    ON d.message_id = m.id AND d.consumer = '{AuditConsumer}'
                WHERE m.event_type = 'AccessDenied' AND m.position > {_identityPosition}
                ORDER BY m.position
                """,
                reader => reader.GetString(1));

            if (deliveries.Count > 0
                && deliveries.All(status => string.Equals(status, "delivered", StringComparison.Ordinal)))
            {
                break;
            }

            await Task.Delay(Eventually.Step);
        }

        await _postgres.ExecuteAsync(
            $"""
            DELETE FROM identity.outbox_deliveries
            WHERE message_id IN (
                SELECT id FROM identity.outbox_messages WHERE position > {_identityPosition})
            """);

        await _postgres.ExecuteAsync(
            $"DELETE FROM identity.outbox_messages WHERE position > {_identityPosition}");
    }

    /// <summary>
    /// Removes the entries the flow appended, leaving the chain it found intact. The table is
    /// append-only, so the trigger is disabled for the delete and enabled again straight after — the
    /// same way the neighbouring suites leave the shared record as they found it.
    /// </summary>
    private async Task RemoveAuditEntriesAsync()
    {
        await _postgres.ExecuteAsync("ALTER TABLE audit.entries DISABLE TRIGGER entries_append_only");

        try
        {
            await _postgres.ExecuteAsync(
                $"DELETE FROM audit.entries WHERE chain_position > {_chainPosition}");
        }
        finally
        {
            await _postgres.ExecuteAsync("ALTER TABLE audit.entries ENABLE TRIGGER entries_append_only");
        }
    }
}
