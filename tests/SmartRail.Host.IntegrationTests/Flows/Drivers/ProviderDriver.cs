using System.Net;
using System.Net.Http.Json;
using SmartRail.Host.Http;
using SmartRail.Host.IntegrationTests.Flows.Support;

namespace SmartRail.Host.IntegrationTests.Flows.Drivers;

/// <summary>
/// The catering provider's write surface in a flow: its callback subscription. The delivery itself is
/// observed where a provider would see it — the stub receiver in <see cref="Support.ProviderReceiver"/> —
/// so the flow proves the channel without reading the delivery table.
/// </summary>
internal sealed class ProviderDriver
{
    /// <summary>The plaintext key the flow's Host is configured with, and the provider calls with.</summary>
    public const string ApiKey = "srk_ab12cd34_5f2c8d7e9b1a4c6d3e0f7a2b8c9d1e4f";

    private readonly HttpClient _client;

    public ProviderDriver(HttpClient client) => _client = client;

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
}
