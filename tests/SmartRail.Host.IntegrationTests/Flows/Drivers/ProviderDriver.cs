using System.Net;
using System.Net.Http.Json;
using SmartRail.Host.Http;
using SmartRail.Host.IntegrationTests.Flows.Support;

namespace SmartRail.Host.IntegrationTests.Flows.Drivers;

/// <summary>
/// The catering provider's surface in a flow: its callback subscription and the delivery rows the
/// dispatcher writes. The API key is the seeded dev key the flow's Host is configured with, and only
/// the subscription this flow registered is touched again when it finishes.
/// </summary>
internal sealed class ProviderDriver
{
    /// <summary>The seeded provider's identifier (<c>seed/catering/providers.json</c>).</summary>
    public static Guid ProviderId { get; } = Guid.Parse("01a15874-e6a0-7546-9097-3dae27a35461");

    /// <summary>The plaintext key the flow's Host is configured with, and the provider calls with.</summary>
    public const string ApiKey = "srk_ab12cd34_5f2c8d7e9b1a4c6d3e0f7a2b8c9d1e4f";

    private readonly HttpClient _client;
    private readonly Infrastructure.PostgresFixture _postgres;

    public ProviderDriver(HttpClient client, Infrastructure.PostgresFixture postgres)
    {
        _client = client;
        _postgres = postgres;
    }

    /// <summary>True once this flow registered a subscription, so cleanup only removes its own.</summary>
    public bool HasSubscription { get; private set; }

    /// <summary>Registers the provider's callback URL, which is how the simulated provider is fed.</summary>
    public async Task RegisterWebhookAsync(string url)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"{ApiRoutes.BasePath}/provider/webhook")
        {
            Content = JsonContent.Create(new { url }),
        };
        request.Headers.Add("X-Api-Key", ApiKey);

        using var response = await _client.SendAsync(request);

        Verify.True(
            response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK,
            "Registering the provider's webhook should be accepted, but it answered "
            + $"{(int)response.StatusCode} {response.StatusCode}");

        HasSubscription = true;
    }

    /// <summary>
    /// Waits until the journey has <paramref name="expected"/> deliveries the dispatcher marked
    /// delivered, so a flow reads the provider's side only once SmartRail considers it applied.
    /// </summary>
    public async Task WaitForDeliveredAsync(Guid journeyId, int expected, string behaviour)
    {
        await Eventually.ValueAsync(
            () => DeliveredCountAsync(journeyId),
            delivered => delivered == expected,
            behaviour);
    }

    /// <summary>How many attempts the dispatcher made for one sequence number of the journey.</summary>
    public async Task<int> AttemptCountAsync(Guid journeyId, long sequenceNumber)
    {
        var counts = await _postgres.QueryAsync(
            $"""
            SELECT attempt_count FROM catering.webhook_deliveries
            WHERE journey_id = '{journeyId}' AND sequence_number = {sequenceNumber}
            """,
            reader => reader.GetInt32(0));

        return counts.Count == 0 ? 0 : counts[0];
    }

    /// <summary>
    /// Removes the subscription this flow registered and its deliveries, leaving no callback behind for
    /// the next test class to receive.
    /// </summary>
    public async Task CleanUpAsync()
    {
        if (!HasSubscription)
        {
            return;
        }

        await _postgres.ExecuteAsync(
            $"""
            DELETE FROM catering.webhook_deliveries
            WHERE subscription_id IN (
                SELECT id FROM catering.webhook_subscriptions WHERE provider_id = '{ProviderId}')
            """);

        await _postgres.ExecuteAsync(
            $"DELETE FROM catering.webhook_subscriptions WHERE provider_id = '{ProviderId}'");
    }

    private async Task<int> DeliveredCountAsync(Guid journeyId)
    {
        var count = Assert.Single(await _postgres.QueryAsync(
            $"""
            SELECT count(*) FROM catering.webhook_deliveries
            WHERE journey_id = '{journeyId}' AND status = 'delivered'
            """,
            reader => reader.GetInt64(0)));

        return (int)count;
    }
}
