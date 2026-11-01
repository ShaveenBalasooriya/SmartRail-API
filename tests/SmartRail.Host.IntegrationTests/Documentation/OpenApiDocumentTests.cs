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
