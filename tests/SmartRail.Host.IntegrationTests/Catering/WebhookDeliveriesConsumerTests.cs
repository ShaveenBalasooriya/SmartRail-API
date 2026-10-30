using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SmartRail.Host.Http;
using SmartRail.Host.IntegrationTests.Infrastructure;
using SmartRail.LiveOperations.Contracts;
using SmartRail.RailNetwork.Contracts;
using SmartRail.RailNetwork.Infrastructure.Persistence;
using SmartRail.SharedKernel.Application.Events;
using SmartRail.SharedKernel.Application.Abstractions;

namespace SmartRail.Host.IntegrationTests.Catering;

/// <summary>
/// Catering's consumer of the event bus (webhook-delivery spec, "Pushed event types", "One delivery
/// per event per subscription"; design D1). A published cancellation creates one delivery for each
/// active subscription, a departure creates none, and delivering the same message twice still leaves
/// one row.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class WebhookDeliveriesConsumerTests(PostgresFixture postgres)
{
    /// <summary>The same dev provider key the other provider tests use, so the shared database has one key row.</summary>
    private const string PlaintextProviderKey = "srk_ab12cd34_5f2c8d7e9b1a4c6d3e0f7a2b8c9d1e4f";

    private const string ProviderKeySetting = "Catering:ProviderApiKey";

    /// <summary>The seeded provider (seed/catering/providers.json).</summary>
    private static readonly Guid ProviderId = Guid.Parse("01a15874-e6a0-7546-9097-3dae27a35461");

    private static readonly Guid GuardStaffId = Guid.Parse("01a15874-e69d-73b2-b24d-d85d22fdead2");

    private readonly List<Guid> _outboxMessageIds = [];

    [Fact]
    public async Task A_cancellation_creates_one_delivery_an_earlier_departure_none_and_a_redelivery_no_second_row()
    {
        // The dispatcher never runs in this test, so the delivery stays pending and can be asserted.
        using var factory = NewFactory();
        var today = RailwayToday();
        var journeyId = await CreateJourneyAsync(factory, today);
        await RegisterWebhookAsync(factory, "https://provider.test/hooks/smartrail");

        try
        {
            var eventId = await PublishCancellationAsync(factory, journeyId);

            await WaitForAsync(
                $"SELECT count(*) FROM catering.webhook_deliveries WHERE journey_id = '{journeyId}'",
                expected: 1);

            var delivery = Assert.Single(await postgres.QueryAsync(
                $"""
                SELECT event_type, sequence_number, status, attempt_count, payload::text
                FROM catering.webhook_deliveries
                WHERE journey_id = '{journeyId}'
                """,
                reader => new DeliveryRow(
                    reader.GetString(0),
                    reader.GetInt64(1),
                    reader.GetString(2),
                    reader.GetInt32(3),
                    reader.GetString(4))));

            Assert.Equal("cancellation_published", delivery.EventType);
            Assert.Equal(1, delivery.SequenceNumber);
            Assert.Equal("pending", delivery.Status);
            Assert.Equal(0, delivery.AttemptCount);

            using var payload = JsonDocument.Parse(delivery.Payload);
            var root = payload.RootElement;

            Assert.Equal(1, root.GetProperty("version").GetInt32());
            Assert.Equal(eventId, root.GetProperty("eventId").GetGuid());
            Assert.Equal(journeyId, root.GetProperty("journeyId").GetGuid());
            Assert.Equal("cancellation_published", root.GetProperty("type").GetString());
            Assert.Equal(1, root.GetProperty("sequenceNumber").GetInt64());
            Assert.Equal("automatically_published", root.GetProperty("provenance").GetString());
            Assert.Empty(root.GetProperty("values").EnumerateObject());
            Assert.Equal(JsonValueKind.Null, root.GetProperty("stopSequence").ValueKind);
            Assert.Equal(JsonValueKind.Null, root.GetProperty("correctsEventId").ValueKind);

            // The bus delivers at least once: the same message delivered again writes no second row.
            await RedeliverAsync(journeyId);
            await Task.Delay(TimeSpan.FromSeconds(3));

            Assert.Equal(1, await CountAsync(
                $"SELECT count(*) FROM catering.webhook_deliveries WHERE journey_id = '{journeyId}'"));

            // A departure is not pushed, so the consumer records nothing for it.
            var departureJourneyId = Guid.CreateVersion7();
            await RecordJourneyStateChangeAsync(
                departureJourneyId,
                Guid.CreateVersion7(),
                sequenceNumber: 1,
                streamPosition: 1,
                eventType: 0);

            await Task.Delay(TimeSpan.FromSeconds(3));

            Assert.Equal(0, await CountAsync(
                $"SELECT count(*) FROM catering.webhook_deliveries WHERE journey_id = '{departureJourneyId}'"));
        }
        finally
        {
            await CleanUpAsync(journeyId);
        }
    }

    private SmartRailApiFactory NewFactory() =>
        new(
            postgres.ConnectionString,
            settings: new Dictionary<string, string>
            {
                [ProviderKeySetting] = PlaintextProviderKey,

                // One poll a day: the dispatcher is not part of what this test asserts.
                ["Catering:Webhook:PollIntervalSeconds"] = "86400",
            });

    private static DateOnly RailwayToday() => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTime(
            DateTimeOffset.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Colombo")).DateTime);

    /// <summary>Creates a journey's live state through the real generation announcement.</summary>
    private async Task<Guid> CreateJourneyAsync(SmartRailApiFactory factory, DateOnly serviceDate)
    {
        using var client = factory.CreateApiClient();
        using var health = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        var journeyId = Guid.CreateVersion7();
        var departAt = new DateTimeOffset(serviceDate.ToDateTime(new TimeOnly(2, 0)), TimeSpan.Zero);

        using var scope = factory.Services.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredKeyedService<IIntegrationEventPublisher>(
            IntegrationEventPublisherKeys.Network);
        var database = scope.ServiceProvider.GetRequiredService<RailNetworkDbContext>();

        publisher.Publish(
            new JourneyGenerated(
                journeyId,
                Guid.CreateVersion7(),
                serviceDate,
                Guid.CreateVersion7(),
                ServiceDirection.Outbound,
                GuardStaffId,
                [
                    new JourneyStopDto(1, Guid.CreateVersion7(), true, null, departAt, Guid.CreateVersion7()),
                    new JourneyStopDto(
                        2,
                        Guid.CreateVersion7(),
                        true,
                        departAt.AddMinutes(10),
                        departAt.AddMinutes(11),
                        Guid.CreateVersion7()),
                    new JourneyStopDto(3, Guid.CreateVersion7(), true, departAt.AddMinutes(20), null, null),
                ]),
            journeyId);

        await database.SaveChangesAsync();

        await WaitForAsync(
            $"SELECT count(*) FROM operations.journey_live_states WHERE journey_id = '{journeyId}'",
            expected: 1);

        return journeyId;
    }

    private static async Task RegisterWebhookAsync(SmartRailApiFactory factory, string url)
    {
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(HttpMethod.Put, $"{ApiRoutes.BasePath}/provider/webhook")
        {
            Content = JsonContent.Create(new { url }),
        };
        request.Headers.Add("X-Api-Key", PlaintextProviderKey);

        using var response = await client.SendAsync(request);

        Assert.True(
            response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK,
            $"registration answered {response.StatusCode}.");
    }

    /// <summary>Publishes a cancellation as the controller and returns the event it recorded.</summary>
    private async Task<Guid> PublishCancellationAsync(SmartRailApiFactory factory, Guid journeyId)
    {
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{ApiRoutes.BasePath}/operations/journeys/{journeyId}/cancellation")
            .WithStaffToken(TestTokens.Create("controller.mahesh", Roles.NetworkController));

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return Assert.Single(await postgres.QueryAsync(
            $"SELECT id FROM operations.journey_events WHERE journey_id = '{journeyId}'",
            reader => reader.GetGuid(0)));
    }

    /// <summary>Records the same journey state change a second time, as a redelivery would.</summary>
    private async Task RedeliverAsync(Guid journeyId)
    {
        var messages = await postgres.QueryAsync(
            $"""
            SELECT event_type, payload
            FROM operations.outbox_messages
            WHERE ordering_key = '{journeyId}' AND event_type = 'JourneyStateChanged'
            ORDER BY position DESC
            LIMIT 1
            """,
            reader => (Type: reader.GetString(0), Payload: reader.GetString(1)));

        var message = Assert.Single(messages);

        await RecordOutboxMessageAsync(journeyId, message.Type, message.Payload);
    }

    private async Task RecordJourneyStateChangeAsync(
        Guid journeyId,
        Guid eventId,
        long sequenceNumber,
        long streamPosition,
        int eventType)
    {
        var payload = JsonSerializer.Serialize(new
        {
            JourneyId = journeyId,
            EventId = eventId,
            SequenceNumber = sequenceNumber,
            StreamPosition = streamPosition,
            EventType = eventType,
        });

        await RecordOutboxMessageAsync(journeyId, nameof(JourneyStateChanged), payload);
    }

    private async Task RecordOutboxMessageAsync(Guid orderingKey, string eventType, string payload)
    {
        var messageId = Guid.CreateVersion7();
        _outboxMessageIds.Add(messageId);

        await postgres.ExecuteAsync(
            $"""
            INSERT INTO operations.outbox_messages (id, event_type, ordering_key, payload, occurred_at)
            VALUES ('{messageId}', '{eventType}', '{orderingKey}', '{payload.Replace("'", "''")}', now())
            """);
    }

    private async Task WaitForAsync(string countSql, int expected)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);

        while (await CountAsync(countSql) != expected && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        Assert.Equal(expected, await CountAsync(countSql));
    }

    private async Task<int> CountAsync(string sql)
    {
        var count = Assert.Single(await postgres.QueryAsync(sql, reader => reader.GetInt64(0)));
        return (int)count;
    }

    private async Task CleanUpAsync(Guid journeyId)
    {
        await postgres.ExecuteAsync(
            $"""
            DELETE FROM catering.webhook_deliveries
            WHERE subscription_id IN (
                SELECT id FROM catering.webhook_subscriptions WHERE provider_id = '{ProviderId}')
            """);
        await postgres.ExecuteAsync(
            $"DELETE FROM catering.webhook_subscriptions WHERE provider_id = '{ProviderId}'");

        foreach (var messageId in _outboxMessageIds)
        {
            await postgres.ExecuteAsync(
                $"DELETE FROM operations.outbox_deliveries WHERE message_id = '{messageId}'");
            await postgres.ExecuteAsync($"DELETE FROM operations.outbox_messages WHERE id = '{messageId}'");
        }

        await postgres.ExecuteAsync(
            $"""
            DELETE FROM operations.outbox_deliveries
            WHERE message_id IN (
                SELECT id FROM operations.outbox_messages WHERE ordering_key = '{journeyId}')
            """);
        await postgres.ExecuteAsync(
            $"DELETE FROM operations.outbox_messages WHERE ordering_key = '{journeyId}'");
        await postgres.ExecuteAsync(
            $"DELETE FROM operations.status_changes WHERE journey_id = '{journeyId}'");
        await postgres.ExecuteAsync(
            $"DELETE FROM operations.journey_events WHERE journey_id = '{journeyId}'");
        await postgres.ExecuteAsync(
            $"DELETE FROM operations.journey_stop_live_states WHERE journey_id = '{journeyId}'");
        await postgres.ExecuteAsync(
            $"DELETE FROM operations.journey_live_states WHERE journey_id = '{journeyId}'");

        // The publish recorded an audit entry; the append-only table only accepts a delete with its
        // trigger off, and the entry must not be left behind.
        await postgres.ExecuteAsync("ALTER TABLE audit.entries DISABLE TRIGGER entries_append_only");

        try
        {
            await postgres.ExecuteAsync(
                $"DELETE FROM audit.entries WHERE target_id = '{journeyId}'");
        }
        finally
        {
            await postgres.ExecuteAsync("ALTER TABLE audit.entries ENABLE TRIGGER entries_append_only");
        }
    }

    private sealed record DeliveryRow(
        string EventType,
        long SequenceNumber,
        string Status,
        int AttemptCount,
        string Payload);
}
