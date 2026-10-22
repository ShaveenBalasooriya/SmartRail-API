using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using SmartRail.Host.IntegrationTests.Infrastructure;
using SmartRail.Host.Http;
using SmartRail.SharedKernel.Application.Abstractions;

namespace SmartRail.Host.IntegrationTests.Authorization;

/// <summary>
/// The provider API-key surface against a real database (design D7, provider-api-keys spec): the
/// seeded key authenticates its provider, and a missing, wrong, unknown or revoked key is refused
/// with the same 401. Staff and provider credentials are not interchangeable (role-based-access
/// spec).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ProviderAuthenticationTests : IDisposable
{
    /// <summary>
    /// The key the tests supply through configuration; it is the same value the seed test uses, so
    /// the shared database keeps exactly one provider key row whichever test starts first.
    /// </summary>
    private const string PlaintextProviderKey = "srk_ab12cd34_5f2c8d7e9b1a4c6d3e0f7a2b8c9d1e4f";

    /// <summary>The same prefix with a different secret, so the lookup succeeds but the hash does not.</summary>
    private const string WrongSecretKey = "srk_ab12cd34_00000000000000000000000000000000";

    /// <summary>A well-formed key whose prefix belongs to no stored key.</summary>
    private const string UnknownPrefixKey = "srk_zz99yy88_5f2c8d7e9b1a4c6d3e0f7a2b8c9d1e4f";

    /// <summary>The prefix shared by the seeded key and the wrong-secret key.</summary>
    private const string SeededKeyPrefix = "ab12cd34";

    /// <summary>The setting the seeder reads the plaintext key from.</summary>
    private const string ProviderKeySetting = "Catering:ProviderApiKey";

    /// <summary>The provider named by seed/identity/provider-key.json.</summary>
    private static readonly Guid ProviderId = Guid.Parse("01a15874-e6a0-7546-9097-3dae27a35461");

    private const string IdentityMeRoute = $"{ApiRoutes.BasePath}/identity/me";

    private readonly PostgresFixture _postgres;
    private readonly SmartRailApiFactory _factory;
    private readonly HttpClient _client;

    public ProviderAuthenticationTests(PostgresFixture postgres)
    {
        _postgres = postgres;
        _factory = new SmartRailApiFactory(
            postgres.ConnectionString,
            settings: new Dictionary<string, string>
            {
                [ProviderKeySetting] = PlaintextProviderKey,
            });
        _client = _factory.CreateApiClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task The_seeded_key_is_served_as_its_provider_and_exposes_the_provider_id()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, TestEndpoints.ProviderRoute)
            .WithApiKey(PlaintextProviderKey);

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ProviderIdentityResponse>();
        Assert.NotNull(body);
        Assert.Equal(ProviderId.ToString(), body.ProviderId);
    }

    [Fact]
    public async Task A_request_with_no_key_is_unauthorized_with_problem_details()
    {
        using var response = await _client.GetAsync(TestEndpoints.ProviderRoute);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(401, document.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task A_wrong_secret_with_a_valid_prefix_is_unauthorized_with_the_same_body_as_an_unknown_prefix()
    {
        using var wrongSecretResponse = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, TestEndpoints.ProviderRoute)
                .WithApiKey(WrongSecretKey));

        using var unknownPrefixResponse = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, TestEndpoints.ProviderRoute)
                .WithApiKey(UnknownPrefixKey));

        Assert.Equal(HttpStatusCode.Unauthorized, wrongSecretResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownPrefixResponse.StatusCode);

        // The correlation ID is the only field that may differ between two requests.
        Assert.Equal(
            await BodyWithoutCorrelationIdAsync(unknownPrefixResponse),
            await BodyWithoutCorrelationIdAsync(wrongSecretResponse));
    }

    [Fact]
    public async Task A_revoked_key_is_unauthorized()
    {
        await _postgres.ExecuteAsync(
            $"UPDATE identity.provider_api_keys SET revoked_at = now() WHERE key_prefix = '{SeededKeyPrefix}'");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, TestEndpoints.ProviderRoute)
                .WithApiKey(PlaintextProviderKey);

            using var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        finally
        {
            // The container, and so the key, is shared with the other test classes.
            await _postgres.ExecuteAsync(
                $"UPDATE identity.provider_api_keys SET revoked_at = NULL WHERE key_prefix = '{SeededKeyPrefix}'");
        }
    }

    [Fact]
    public async Task A_staff_token_does_not_authenticate_a_provider_endpoint()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, TestEndpoints.ProviderRoute)
            .WithStaffToken(TestTokens.Create("controller.mahesh", Roles.NetworkController));

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_provider_key_does_not_authenticate_a_staff_endpoint()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, IdentityMeRoute)
            .WithApiKey(PlaintextProviderKey);

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>The problem body with the per-request correlation ID removed, for comparison.</summary>
    private static async Task<string> BodyWithoutCorrelationIdAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        body.Remove("traceId");

        return body.ToJsonString();
    }
}

/// <summary>Adds the provider key header to a request.</summary>
internal static class ProviderKeyRequestExtensions
{
    public static HttpRequestMessage WithApiKey(this HttpRequestMessage request, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Headers.Add(ApiKeyHeader, apiKey);
        return request;
    }

    private const string ApiKeyHeader = "X-Api-Key";
}
