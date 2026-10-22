using System.Net;
using System.Text.Json;
using SmartRail.Host.IntegrationTests.Infrastructure;

namespace SmartRail.Host.IntegrationTests.Documentation;

[Collection(PostgresCollection.Name)]
public sealed class OpenApiDocumentTests(PostgresFixture postgres) : IDisposable
{
    private readonly SmartRailApiFactory _factory = new(postgres.ConnectionString, environment: "E2E");

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
    public async Task The_scalar_reference_is_served()
    {
        using var client = _factory.CreateApiClient();
        using var response = await client.GetAsync("/scalar/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
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
