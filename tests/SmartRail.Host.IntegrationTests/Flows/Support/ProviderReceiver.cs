using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SmartRail.Host.IntegrationTests.Flows.Support;

/// <summary>One webhook delivery as the provider receives it (webhook-delivery payload, version 1).</summary>
/// <param name="EventId">The journey event this delivery carries.</param>
/// <param name="SequenceNumber">The delivery's place in its subscription's sequence for the journey.</param>
/// <param name="Type">The event's type, for example <c>delay_published</c>.</param>
/// <param name="JourneyId">The journey the event changed.</param>
/// <param name="StopSequence">The stop the change names, when it names one.</param>
/// <param name="StationId">The station the change names, when it names one.</param>
/// <param name="Values">The change's own values, read as text.</param>
/// <param name="PublishedAt">When the change was published.</param>
/// <param name="Provenance">How the change reached the channels.</param>
/// <param name="CorrectsEventId">The event a correction withdraws, when the event is a correction.</param>
internal sealed record ProviderWebhookPayload(
    Guid EventId,
    long SequenceNumber,
    string Type,
    Guid JourneyId,
    int? StopSequence,
    Guid? StationId,
    IReadOnlyDictionary<string, string?> Values,
    DateTimeOffset PublishedAt,
    string Provenance,
    Guid? CorrectsEventId);

/// <summary>
/// A stub provider receiver on Kestrel: it records every webhook delivery SmartRail pushes and answers
/// 200, or 503 for a fixed number of first attempts, which is the provider outage the retry schedule
/// exists for. It is the simulated provider of these flows, run in-process so the checks stay at Layer A:
/// the real subscription, dispatcher, delivery rows and retry are used, only the receiving server is a stub.
/// </summary>
internal sealed class ProviderReceiver : IAsyncDisposable
{
    /// <summary>How long a delivery may take to arrive before a flow fails.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(25);

    private readonly WebApplication _app;
    private readonly int _failuresBeforeSuccess;
    private int _requests;

    private ProviderReceiver(WebApplication app, int failuresBeforeSuccess)
    {
        _app = app;
        _failuresBeforeSuccess = failuresBeforeSuccess;
    }

    /// <summary>Every delivery the receiver was sent, in the order the provider would read them.</summary>
    public ConcurrentQueue<ProviderWebhookPayload> Deliveries { get; } = new();

    /// <summary>The URL SmartRail is told to call.</summary>
    public string CallbackUrl { get; private set; } = string.Empty;

    /// <summary>Starts a receiver that answers 200, or 503 for the first <paramref name="failuresBeforeSuccess"/> attempts.</summary>
    public static async Task<ProviderReceiver> StartAsync(int failuresBeforeSuccess = 0)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var app = builder.Build();
        var receiver = new ProviderReceiver(app, failuresBeforeSuccess);

        app.MapPost("/webhook", async (HttpContext context) =>
        {
            using var document = await JsonDocument.ParseAsync(context.Request.Body);

            receiver.Deliveries.Enqueue(Read(document.RootElement));

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

    /// <summary>Waits until the receiver has been sent at least <paramref name="count"/> deliveries.</summary>
    public async Task WaitForDeliveriesAsync(int count, string behaviour)
    {
        var deadline = DateTimeOffset.UtcNow.Add(Timeout);

        while (Deliveries.Count < count && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        Assert.True(
            Deliveries.Count >= count,
            $"{behaviour}, but the provider stub had received {Deliveries.Count} delivery(ies) "
            + $"instead of at least {count} within {Timeout.TotalSeconds} seconds.");
    }

    /// <summary>
    /// Watches the receiver for a short window and fails if another delivery arrives. For the check
    /// that a change is delivered exactly once.
    /// </summary>
    public async Task WaitQuietAsync(TimeSpan window, int expectedCount, string behaviour)
    {
        var until = DateTimeOffset.UtcNow.Add(window);

        while (DateTimeOffset.UtcNow < until)
        {
            Assert.True(
                Deliveries.Count <= expectedCount,
                $"{behaviour}, but the provider stub had received {Deliveries.Count} delivery(ies) "
                + $"instead of {expectedCount}.");
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        Assert.True(
            Deliveries.Count == expectedCount,
            $"{behaviour}, but the provider stub had received {Deliveries.Count} delivery(ies) "
            + $"instead of {expectedCount}.");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    /// <summary>Reads one v1 payload, keeping the values a flow asserts on rather than the JSON.</summary>
    private static ProviderWebhookPayload Read(JsonElement payload) => new(
        payload.GetProperty("eventId").GetGuid(),
        payload.GetProperty("sequenceNumber").GetInt64(),
        payload.GetProperty("type").GetString()!,
        payload.GetProperty("journeyId").GetGuid(),
        OptionalInt(payload, "stopSequence"),
        OptionalGuid(payload, "stationId"),
        payload.GetProperty("values").EnumerateObject().ToDictionary(
            property => property.Name,
            property => property.Value.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.String => property.Value.GetString(),
                _ => property.Value.GetRawText(),
            }),
        payload.GetProperty("publishedAt").GetDateTimeOffset(),
        payload.GetProperty("provenance").GetString()!,
        OptionalGuid(payload, "correctsEventId"));

    private static int? OptionalInt(JsonElement payload, string property) =>
        payload.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.Number
            ? value.GetInt32()
            : null;

    private static Guid? OptionalGuid(JsonElement payload, string property) =>
        payload.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.String
            ? value.GetGuid()
            : null;
}
