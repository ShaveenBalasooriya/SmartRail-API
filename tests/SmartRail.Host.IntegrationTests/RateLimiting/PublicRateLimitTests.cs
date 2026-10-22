using System.Net;
using System.Text.Json;
using SmartRail.Host.IntegrationTests.Infrastructure;
using SmartRail.Host.Http;

namespace SmartRail.Host.IntegrationTests.RateLimiting;

/// <summary>
/// The public rate limit against a deliberately low configured limit (design D8): the limit is per
/// client, and a refusal is problem-details 429 with <c>Retry-After</c> (api-rate-limiting spec).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PublicRateLimitTests : IDisposable
{
    private const string StationsRoute = $"{ApiRoutes.BasePath}/network/stations";

    private readonly SmartRailApiFactory _factory;
    private readonly HttpClient _client;

    public PublicRateLimitTests(PostgresFixture postgres)
    {
        _factory = new SmartRailApiFactory(
            postgres.ConnectionString,
            settings: new Dictionary<string, string>
            {
                ["RateLimiting:Public:PermitLimit"] = "2",
            });
        _client = _factory.CreateApiClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task A_public_request_over_the_limit_is_refused_with_retry_after()
    {
        using var first = await _client.GetAsync(StationsRoute);
        using var second = await _client.GetAsync(StationsRoute);
        using var third = await _client.GetAsync(StationsRoute);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Equal("application/problem+json", third.Content.Headers.ContentType?.MediaType);
        Assert.True(third.Headers.RetryAfter is not null, "a 429 must tell the client when to retry");

        using var document = JsonDocument.Parse(await third.Content.ReadAsStringAsync());
        Assert.Equal(429, document.RootElement.GetProperty("status").GetInt32());
    }
}
