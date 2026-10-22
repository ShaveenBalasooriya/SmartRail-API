using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartRail.Host.IntegrationTests.Infrastructure;
using SmartRail.SharedKernel.Application.Events;
using SmartRail.SharedKernel.Infrastructure;

namespace SmartRail.Host.IntegrationTests.EventBus;

/// <summary>
/// The failure-visibility half of task 3.1 (event-delivery spec: failure visibility, design D10). A
/// consumer that always throws makes one delivery fail: the failure counter rises with exactly the
/// event type and consumer name as tags, and the log entry names the event, the consumer and the
/// attempt while carrying neither the payload nor the exception object.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DeliveryFailureVisibilityTests(PostgresFixture postgres)
{
    /// <summary>The meter the OpenTelemetry exporter will listen to (design D10).</summary>
    private const string MeterName = "SmartRail.Events";

    /// <summary>Every failed delivery attempt, which is the counter this test watches rise.</summary>
    private const string FailureCounterName = "smartrail.events.delivery_failures";

    [Fact]
    public async Task A_failed_attempt_raises_the_failure_counter_and_logs_the_event_without_the_payload()
    {
        var eventId = Guid.CreateVersion7();

        // A value only the payload carries, so the log can be shown not to contain it.
        var payloadSecret = $"payload-must-not-be-logged-{Guid.CreateVersion7():N}";

        // A first start applies the identity outbox migration, then stops, so the row below is
        // recorded while no dispatcher runs and this test's consumer sees only its own event.
        await StartAndStopAsync();

        var payload = JsonSerializer.Serialize(new FailureProbeEvent(eventId, payloadSecret));

        var recorded = await postgres.ExecuteAsync(
            "INSERT INTO identity.outbox_messages (id, event_type, ordering_key, payload, occurred_at) "
            + $"VALUES ('{eventId}', '{nameof(FailureProbeEvent)}', NULL, '{payload}', now())");

        Assert.Equal(1, recorded);

        // The listener runs in this process, which is what dotnet-counters does from outside it.
        using var measurements = new FailureCounterListener();

        using var factory = CreateFactory();
        using var client = factory.CreateApiClient();
        await ReadyAsync(client);

        // The first attempt fails at once; the retries are minutes apart, so no more are waited for.
        var entry = await WaitForFailedAttemptAsync(factory.Logs, eventId);

        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains(eventId.ToString(), entry.Message, StringComparison.Ordinal);
        Assert.Contains(FailureProbeConsumer.ConsumerName, entry.Message, StringComparison.Ordinal);
        Assert.Contains("attempt 1", entry.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(nameof(InvalidOperationException), entry.Message, StringComparison.Ordinal);

        // The exception object is not logged either, so nothing it carries can leak.
        Assert.Null(entry.Exception);

        // The whole capture, not just this entry: no log line may carry the payload.
        Assert.DoesNotContain(payloadSecret, factory.Logs.AllText, StringComparison.Ordinal);

        var failures = measurements.For(FailureCounterName)
            .Where(measurement => Equals(measurement.Tags["event_type"], nameof(FailureProbeEvent)))
            .ToList();

        Assert.NotEmpty(failures);
        Assert.All(
            failures,
            failure =>
            {
                // Exactly the two tags design D10 allows: nothing else, and nothing from the payload.
                Assert.Equal(2, failure.Tags.Count);
                Assert.Equal(nameof(FailureProbeEvent), failure.Tags["event_type"]);
                Assert.Equal(FailureProbeConsumer.ConsumerName, failure.Tags["consumer"]);
            });
    }

    /// <summary>Starts the real Host once, which applies migrations, and stops it again.</summary>
    private async Task StartAndStopAsync()
    {
        using var factory = new SmartRailApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();

        var ready = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
    }

    /// <summary>A Host that delivers this test's probe event to the consumer that always throws.</summary>
    private SmartRailApiFactory CreateFactory() => new(
        postgres.ConnectionString,
        configureServices: services => services.AddEventConsumer<FailureProbeEvent, FailureProbeConsumer>());

    private static async Task ReadyAsync(HttpClient client)
    {
        var ready = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
    }

    /// <summary>Waits up to ten seconds for the dispatcher's first failed attempt to be logged.</summary>
    private static async Task<LogEntry> WaitForFailedAttemptAsync(LogCapture logs, Guid eventId)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);

        while (DateTimeOffset.UtcNow < deadline)
        {
            var entry = logs.Entries.FirstOrDefault(captured =>
                captured.Category == "SmartRail.SharedKernel.Infrastructure.Events.PostgresEventTransport"
                && captured.Message.Contains(eventId.ToString(), StringComparison.Ordinal));

            if (entry is not null)
            {
                return entry;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        Assert.Fail($"No failed-attempt log entry was written for event {eventId} within ten seconds.");
        return null!;
    }

    /// <summary>The event this test records, so no other test's consumer competes for it.</summary>
    public sealed record FailureProbeEvent(Guid Id, string Note);

    /// <summary>The consumer that always throws, which is how a failed attempt is forced.</summary>
    public sealed class FailureProbeConsumer : IEventConsumer<FailureProbeEvent>
    {
        public const string ConsumerName = "test.failure-probe";

        public string Name => ConsumerName;

        public Task Handle(FailureProbeEvent integrationEvent, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The failure probe consumer always throws.");
    }

    /// <summary>
    /// Reads this process's metrics, which is what an exporter (or <c>dotnet-counters</c>) does: every
    /// long measurement on the events meter, with its tags, kept so the test can assert on them.
    /// </summary>
    private sealed class FailureCounterListener : IDisposable
    {
        private readonly ConcurrentBag<Measurement> _measurements = [];
        private readonly MeterListener _listener;

        public FailureCounterListener()
        {
            _listener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name == MeterName)
                    {
                        listener.EnableMeasurementEvents(instrument);
                    }
                },
            };

            _listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
            {
                var values = new Dictionary<string, object?>(StringComparer.Ordinal);

                foreach (var tag in tags)
                {
                    values[tag.Key] = tag.Value;
                }

                // A counter's value is its increment, so one measurement is one failed attempt.
                _measurements.Add(new Measurement(instrument.Name, values));
            });

            _listener.Start();
        }

        /// <summary>Every measurement recorded so far for <paramref name="instrument"/>.</summary>
        public IReadOnlyList<Measurement> For(string instrument) =>
            [.. _measurements.Where(measurement => measurement.Instrument == instrument)];

        public void Dispose() => _listener.Dispose();
    }

    /// <summary>One recorded counter increment: which instrument, and the tags it was counted with.</summary>
    private sealed record Measurement(string Instrument, IReadOnlyDictionary<string, object?> Tags);
}
