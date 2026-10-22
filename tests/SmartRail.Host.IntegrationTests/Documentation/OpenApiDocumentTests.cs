using System.Net;
using System.Text.Json;
using SmartRail.Host.IntegrationTests.Infrastructure;

namespace SmartRail.Host.IntegrationTests.Documentation;

[Collection(PostgresCollection.Name)]
public sealed class OpenApiDocumentTests(PostgresFixture postgres) : IDisposable
{
    /// <summary>
    /// The provider key the tests configure. It must never appear in the document or the reference
    /// page: the application pre-fills no credential.
    /// </summary>
    private const string PlaintextProviderKey = "srk_ab12cd34_5f2c8d7e9b1a4c6d3e0f7a2b8c9d1e4f";

    private readonly SmartRailApiFactory _factory = new(
        postgres.ConnectionString,
        environment: "E2E",
        settings: new Dictionary<string, string>
        {
            ["Catering:ProviderApiKey"] = PlaintextProviderKey,
        });

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task The_document_is_valid_and_lists_the_application_endpoints()
    {
        using var client = _factory.CreateApiClient();
        using var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.True(root.TryGetProperty("openapi", out var version));
        Assert.StartsWith("3.", version.GetString()!, StringComparison.Ordinal);

        var paths = root.GetProperty("paths").EnumerateObject().Select(property => property.Name).ToList();
        Assert.Contains(TestEndpoints.ConventionsRoute, paths);
        Assert.Contains(TestEndpoints.ValidationRoute, paths);
    }

    [Fact]
    public async Task An_endpoint_that_validates_input_documents_its_400_problem_details_response()
    {
        using var client = _factory.CreateApiClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var operation = document.RootElement
            .GetProperty("paths")
            .GetProperty(TestEndpoints.ValidationRoute)
            .GetProperty("post");

        var badRequest = operation.GetProperty("responses").GetProperty("400");
        Assert.True(badRequest.TryGetProperty("content", out var content));
        Assert.True(content.TryGetProperty("application/problem+json", out _));
    }

    [Fact]
    public async Task An_endpoint_documents_the_problem_details_500_response()
    {
        using var client = _factory.CreateApiClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var operation = document.RootElement
            .GetProperty("paths")
            .GetProperty(TestEndpoints.FailureRoute)
            .GetProperty("get");

        Assert.True(operation.GetProperty("responses").TryGetProperty("500", out _));
    }

    [Fact]
    public async Task The_document_declares_the_staff_oauth2_and_provider_api_key_schemes_and_applies_them_per_endpoint()
    {
        using var client = _factory.CreateApiClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        var root = document.RootElement;

        // Each endpoint declares its own requirement; there is no document-level one that a public
        // endpoint could inherit.
        Assert.False(root.TryGetProperty("security", out _));

        var schemes = root
            .GetProperty("components")
            .GetProperty("securitySchemes");

        var staff = schemes.GetProperty("staff");
        Assert.Equal("oauth2", staff.GetProperty("type").GetString());
        Assert.False(staff.TryGetProperty("scheme", out _));
        Assert.False(staff.TryGetProperty("bearerFormat", out _));

        var staffFlow = staff.GetProperty("flows").GetProperty("authorizationCode");
        Assert.Equal(TestTokens.AuthorizationUrl, staffFlow.GetProperty("authorizationUrl").GetString());
        Assert.Equal(TestTokens.TokenUrl, staffFlow.GetProperty("tokenUrl").GetString());
        Assert.Equal(TestTokens.TokenUrl, staffFlow.GetProperty("refreshUrl").GetString());

        var staffScopes = staffFlow
            .GetProperty("scopes")
            .EnumerateObject()
            .Select(scope => scope.Name)
            .OrderBy(scope => scope, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["offline_access", "openid", "profile"], staffScopes);

        var provider = schemes.GetProperty("provider");
        Assert.Equal("apiKey", provider.GetProperty("type").GetString());
        Assert.Equal("header", provider.GetProperty("in").GetString());
        Assert.Equal("X-Api-Key", provider.GetProperty("name").GetString());

        var me = root
            .GetProperty("paths")
            .GetProperty("/api/v1/identity/me")
            .GetProperty("get");

        var staffRequirement = Assert.Single(me.GetProperty("security").EnumerateArray());
        var requiredScopes = staffRequirement
            .GetProperty("staff")
            .EnumerateArray()
            .Select(scope => scope.GetString() ?? string.Empty)
            .ToArray();
        Assert.Equal(["openid", "profile", "offline_access"], requiredScopes);

        var stations = root
            .GetProperty("paths")
            .GetProperty("/api/v1/network/stations")
            .GetProperty("get");

        // The empty array is explicit: the operation declares no requirement at all.
        Assert.Empty(stations.GetProperty("security").EnumerateArray());

        var providerEndpoint = root
            .GetProperty("paths")
            .GetProperty(TestEndpoints.ProviderRoute)
            .GetProperty("get");

        var providerRequirement = Assert.Single(providerEndpoint.GetProperty("security").EnumerateArray());
        Assert.True(providerRequirement.TryGetProperty("provider", out var providerScopes));
        Assert.Empty(providerScopes.EnumerateArray());

        // Neither a token nor the provider key is embedded in the document.
        Assert.DoesNotContain("srk_", json, StringComparison.Ordinal);
        Assert.DoesNotContain("eyJ", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_scalar_reference_is_configured_for_the_public_staff_client_with_pkce_and_scopes()
    {
        using var client = _factory.CreateApiClient();
        using var response = await client.GetAsync("/scalar/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);

        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("\"x-scalar-client-id\":\"smartrail-staff\"", html, StringComparison.Ordinal);
        Assert.Contains("\"x-usePkce\":\"SHA-256\"", html, StringComparison.Ordinal);
        Assert.Contains("\"x-scalar-redirect-uri\":\"/scalar/\"", html, StringComparison.Ordinal);
        Assert.Contains(
            "\"selectedScopes\":[\"openid\",\"profile\",\"offline_access\"]",
            html,
            StringComparison.Ordinal);

        // A public client has no secret, and the page pre-fills neither a token nor the provider key.
        Assert.DoesNotContain("clientSecret", html, StringComparison.Ordinal);
        Assert.DoesNotContain("srk_", html, StringComparison.Ordinal);
        Assert.DoesNotContain("eyJ", html, StringComparison.Ordinal);
    }
}

[Collection(PostgresCollection.Name)]
public sealed class DocumentationEnvironmentTests(PostgresFixture postgres) : IDisposable
{
    private readonly SmartRailApiFactory _factory = new(postgres.ConnectionString, environment: "Production");

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task The_document_is_not_served_in_a_production_configuration()
    {
        using var client = _factory.CreateApiClient();
        using var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_scalar_reference_is_not_served_in_a_production_configuration()
    {
        using var client = _factory.CreateApiClient();
        using var response = await client.GetAsync("/scalar/");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
