using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SmartRail.Host.IntegrationTests.Infrastructure;
using SmartRail.SharedKernel.Application.Events;
using SmartRail.SharedKernel.Infrastructure;

namespace SmartRail.Host.IntegrationTests.EventBus;

/// <summary>
/// The at-least-once promise of the event bus (event-delivery spec: restart before delivery). One
/// event recorded while no dispatcher is running is handled exactly once after start-up, and a
/// second start delivers nothing more because the delivery is already recorded.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RestartRedeliveryTests(PostgresFixture postgres)
{
    private readonly RestartProbeConsumer _consumer = new();

    [Fact]
    public async Task An_event_recorded_while_the_dispatcher_was_stopped_is_delivered_exactly_once()
    {
        var eventId = Guid.CreateVersion7();

        // A first start migrates the schema, then stops, so the row below is recorded while no
        // dispatcher runs.
        await StartAndStopAsync();

        var payload = JsonSerializer.Serialize(new RestartProbeEvent(eventId, "recorded while stopped"));

        var recorded = await postgres.ExecuteAsync(
            "INSERT INTO identity.outbox_messages (id, event_type, ordering_key, payload, occurred_at) "
            + $"VALUES ('{eventId}', '{nameof(RestartProbeEvent)}', NULL, '{payload}', now())");

        Assert.Equal(1, recorded);

        // The application starts again and the dispatcher finds the unpublished row.
        using (var secondStart = CreateFactory())
        {
            using var client = secondStart.CreateApiClient();

            await WaitForDeliveryAsync(eventId);

            Assert.Equal(1, _consumer.CountOf(eventId));
        }

        // A third start redelivers nothing: the delivery row is already delivered.
        using (var thirdStart = CreateFactory())
        {
            using var client = thirdStart.CreateApiClient();

            await Task.Delay(TimeSpan.FromSeconds(3));

            Assert.Equal(1, _consumer.CountOf(eventId));
        }
    }

    /// <summary>Starts the real Host once, which applies migrations, and stops it again.</summary>
    private async Task StartAndStopAsync()
    {
        using var factory = new SmartRailApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();

        var ready = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
    }

    /// <summary>
    /// A Host that runs the dispatcher with this test's consumer. The consumer is registered last,
    /// so the bus resolves the instance this test counts rather than a new scoped one.
    /// </summary>
    private SmartRailApiFactory CreateFactory() => new(
        postgres.ConnectionString,
        configureServices: services =>
        {
            services.AddEventConsumer<RestartProbeEvent, RestartProbeConsumer>();
            services.AddSingleton(_consumer);
        });

    /// <summary>Waits up to ten seconds for the dispatcher's one-second poll to deliver.</summary>
    private async Task WaitForDeliveryAsync(Guid eventId)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);

        while (_consumer.CountOf(eventId) == 0 && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
    }

    /// <summary>The event this test records, so no other test's consumer competes for it.</summary>
    public sealed record RestartProbeEvent(Guid Id, string Note);

    /// <summary>Records the event IDs delivered, so a second start can be shown to deliver nothing more.</summary>
    public sealed class RestartProbeConsumer : IEventConsumer<RestartProbeEvent>
    {
        private readonly ConcurrentQueue<Guid> _handledIds = new();

        public string Name => "test.restart-probe";

        /// <summary>How many times <paramref name="eventId"/> has been delivered to this consumer.</summary>
        public int CountOf(Guid eventId) => _handledIds.Count(handled => handled == eventId);

        public Task Handle(RestartProbeEvent integrationEvent, CancellationToken cancellationToken)
        {
            _handledIds.Enqueue(integrationEvent.Id);
            return Task.CompletedTask;
        }
    }
}
