using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartRail.Host.Http;
using SmartRail.Host.IntegrationTests.Infrastructure;
using SmartRail.RailNetwork.Contracts;
using SmartRail.RailNetwork.Infrastructure.Persistence;
using SmartRail.SharedKernel.Application.Abstractions;
using SmartRail.SharedKernel.Application.Events;

namespace SmartRail.Host.IntegrationTests.Catering;

/// <summary>
/// The webhook dispatcher against a stub receiver (webhook-delivery spec, "Retry with backoff",
/// "Order preserved per journey"; design D2). The first attempt fails and the second succeeds, so the
/// delivery is marked delivered with two attempts; while the earlier event is awaiting its retry, the
/// later event of the same journey is not sent.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class WebhookDispatchTests(PostgresFixture postgres)
{
    private const string PlaintextProviderKey = "srk_ab12cd34_5f2c8d7e9b1a4c6d3e0f7a2b8c9d1e4f";

    private const string ProviderKeySetting = "Catering:ProviderApiKey";

    private static readonly Guid ProviderId = Guid.Parse("01a15874-e6a0-7546-9097-3dae27a35461");

    private static readonly Guid GuardStaffId = Guid.Parse("01a15874-e69d-73b2-b24d-d85d22fdead2");

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(25);

    [Fact]
    public async Task A_failed_attempt_is_retried_and_a_later_event_waits_for_the_earlier_one()
    {
        await using var receiver = await StubReceiver.StartAsync(failuresBeforeSuccess: 1);

        using var factory = NewFactory();
        var today = RailwayToday();
        var journeyId = await CreateJourneyAsync(factory, today);

        try
        {
            await RegisterWebhookAsync(factory, receiver.CallbackUrl);

            using var client = factory.CreateApiClient();

            using var delay = await client.SendAsync(DelayRequest(journeyId));
            Assert.Equal(HttpStatusCode.Created, delay.StatusCode);

            using var cancellation = await client.SendAsync(CancellationRequest(journeyId));
            Assert.Equal(HttpStatusCode.Created, cancellation.StatusCode);

            // The first event's first attempt fails and the stub answers 503.
            await receiver.WaitForRequestsAsync(1);

            var first = Assert.Single(receiver.Requests);
            Assert.Equal(1, first.SequenceNumber);
            Assert.Equal("delay_published", first.Type);

            // While event 1 waits for its retry, event 2 of the same journey must not be sent.
            await Task.Delay(TimeSpan.FromMilliseconds(1200));

            Assert.Single(receiver.Requests);
            Assert.Equal(0, await AttemptCountAsync(journeyId, sequenceNumber: 2));

            // The retry succeeds, then event 2 is sent.
            await receiver.WaitForRequestsAsync(3);
            await WaitForDeliveredAsync(journeyId, expected: 2);

            Assert.Equal([1L, 1L, 2L], [.. receiver.Requests.Select(request => request.SequenceNumber)]);
            Assert.Equal(2, await AttemptCountAsync(journeyId, sequenceNumber: 1));
            Assert.Equal(1, await AttemptCountAsync(journeyId, sequenceNumber: 2));

            Assert.Equal(2, await CountAsync(
                $"""
                SELECT count(*) FROM catering.webhook_deliveries
                WHERE journey_id = '{journeyId}' AND status = 'delivered'
                """));
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

                // A fast schedule so the test runs in seconds rather than the spec's 43 minutes.
                ["Catering:Webhook:PollIntervalSeconds"] = "1",
                ["Catering:Webhook:RetryDelaysSeconds"] = "3,3,3,3,3",
                ["Catering:Webhook:RequestTimeoutSeconds"] = "5",
            });

    private static DateOnly RailwayToday() => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTime(
            DateTimeOffset.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Colombo")).DateTime);

    private static HttpRequestMessage DelayRequest(Guid journeyId) =>
        new HttpRequestMessage(HttpMethod.Post, $"{ApiRoutes.BasePath}/operations/journeys/{journeyId}/delays")
        {
            Content = JsonContent.Create(new { fromStopSequence = 2, minutes = 5 }),
        }.WithStaffToken(TestTokens.Create("controller.mahesh", Roles.NetworkController));

    private static HttpRequestMessage CancellationRequest(Guid journeyId) =>
        new HttpRequestMessage(HttpMethod.Post, $"{ApiRoutes.BasePath}/operations/journeys/{journeyId}/cancellation")
            .WithStaffToken(TestTokens.Create("controller.mahesh", Roles.NetworkController));

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

        var deadline = DateTimeOffset.UtcNow.Add(Timeout);

        while (await CountAsync(
                   $"SELECT count(*) FROM operations.journey_live_states WHERE journey_id = '{journeyId}'")
               == 0
               && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

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

    private async Task<int> AttemptCountAsync(Guid journeyId, long sequenceNumber)
    {
        var counts = await postgres.QueryAsync(
            $"""
            SELECT attempt_count FROM catering.webhook_deliveries
            WHERE journey_id = '{journeyId}' AND sequence_number = {sequenceNumber}
            """,
            reader => reader.GetInt32(0));

        return counts.Count == 0 ? 0 : counts[0];
    }

    private async Task WaitForDeliveredAsync(Guid journeyId, int expected)
    {
        var deadline = DateTimeOffset.UtcNow.Add(Timeout);

        while (await CountAsync(
                   $"""
                   SELECT count(*) FROM catering.webhook_deliveries
                   WHERE journey_id = '{journeyId}' AND status = 'delivered'
                   """)
               != expected
               && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        Assert.Equal(expected, await CountAsync(
            $"""
            SELECT count(*) FROM catering.webhook_deliveries
            WHERE journey_id = '{journeyId}' AND status = 'delivered'
            """));
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

        // The publish recorded audit entries; the append-only table only accepts a delete with its
        // trigger off, and the entries must not be left behind.
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

    /// <summary>One request the stub received, as the provider would read it.</summary>
    private sealed record ReceivedRequest(Guid EventId, long SequenceNumber, string? Type);

    /// <summary>
    /// An in-process receiver on Kestrel: it records every payload and answers 503 a fixed number of
    /// times before it succeeds, which is the provider outage the retry schedule exists for.
    /// </summary>
    private sealed class StubReceiver : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly int _failuresBeforeSuccess;
        private int _requests;

        private StubReceiver(WebApplication app, int failuresBeforeSuccess)
        {
            _app = app;
            _failuresBeforeSuccess = failuresBeforeSuccess;
        }

        public ConcurrentQueue<ReceivedRequest> Requests { get; } = new();

        public string CallbackUrl { get; private set; } = string.Empty;

        public static async Task<StubReceiver> StartAsync(int failuresBeforeSuccess)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");

            var app = builder.Build();
            var receiver = new StubReceiver(app, failuresBeforeSuccess);

            app.MapPost("/webhook", async (HttpContext context) =>
            {
                using var document = await JsonDocument.ParseAsync(context.Request.Body);

                receiver.Requests.Enqueue(new ReceivedRequest(
                    document.RootElement.GetProperty("eventId").GetGuid(),
                    document.RootElement.GetProperty("sequenceNumber").GetInt64(),
                    document.RootElement.GetProperty("type").GetString()));

                var requestNumber = Interlocked.Increment(ref receiver._requests);

                return requestNumber <= receiver._failuresBeforeSuccess
                    ? Results.StatusCode(StatusCodes.Status503ServiceUnavailable)
                    : Results.Ok();
            });

            await app.StartAsync();

            var addresses = app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!
                .Addresses;

            receiver.CallbackUrl = $"{addresses.First()}/webhook";

            return receiver;
        }

        /// <summary>Waits until the stub has received at least <paramref name="count"/> requests.</summary>
        public async Task WaitForRequestsAsync(int count)
        {
            var deadline = DateTimeOffset.UtcNow.Add(Timeout);

            while (Requests.Count < count && DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100));
            }

            Assert.True(
                Requests.Count >= count,
                $"the stub received {Requests.Count} request(s), expected {count}.");
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
