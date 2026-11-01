using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SmartRail.Host.IntegrationTests.Infrastructure;
using SmartRail.Host.Http;

namespace SmartRail.Host.IntegrationTests.Conventions;

[Collection(PostgresCollection.Name)]
public sealed class ApiConventionsTests : IDisposable
{
    private readonly SmartRailApiFactory _factory;
    private readonly HttpClient _client;

    public ApiConventionsTests(PostgresFixture postgres)
    {
        _factory = new SmartRailApiFactory(postgres.ConnectionString);
        _client = _factory.CreateApiClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task An_unsupported_media_type_is_rejected_in_the_standard_error_format()
    {
        using var content = new StringContent("name=central&count=1", Encoding.UTF8, "application/x-www-form-urlencoded");
        using var response = await _client.PostAsync(TestEndpoints.ValidationRoute, content);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        await AssertStandardProblemDetailsAsync(response);
    }

    private static async Task AssertStandardProblemDetailsAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.True(root.TryGetProperty("type", out _), "problem details must carry 'type'");
        Assert.True(root.TryGetProperty("title", out _), "problem details must carry 'title'");
        Assert.Equal((int)response.StatusCode, root.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()), "problem details must carry 'traceId'");
    }
}

[Collection(PostgresCollection.Name)]
public sealed class ValidationTests(PostgresFixture postgres) : IDisposable
{
    private readonly SmartRailApiFactory _factory = new(postgres.ConnectionString);
    private HttpClient Client => _factory.CreateApiClient();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Malformed_json_is_rejected_as_a_problem_details_response()
    {
        using var client = Client;
        using var response = await client.PostAsync(
            TestEndpoints.ValidationRoute,
            new StringContent("""{ "name": "central",, }""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(400, document.RootElement.GetProperty("status").GetInt32());
    }
}

[Collection(PostgresCollection.Name)]
public sealed class ProblemDetailsTests(PostgresFixture postgres) : IDisposable
{
    private readonly SmartRailApiFactory _productionFactory = new(postgres.ConnectionString, environment: "Production");

    public void Dispose()
    {
        _productionFactory.Dispose();
    }

    [Fact]
    public async Task An_unhandled_error_outside_development_hides_the_exception_text()
    {
        using var client = _productionFactory.CreateApiClient();
        using var response = await client.GetAsync(TestEndpoints.FailureRoute);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain(TestEndpoints.FailureSecret, body, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", body, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(body);
        Assert.Equal("An unexpected error occurred.", document.RootElement.GetProperty("title").GetString());
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task The_error_is_reported_with_the_correlation_id_and_the_route_template()
    {
        using var client = _productionFactory.CreateApiClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, TestEndpoints.FailureRoute);
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, "correlation-under-test");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        var failure = Assert.Single(
            _productionFactory.Logs.Entries,
            entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Error
                && entry.Message.Contains("Unhandled request failure", StringComparison.Ordinal));

        Assert.Equal("correlation-under-test", failure.Value("CorrelationId"));
        Assert.Equal(
            $"{TestEndpoints.FailureRoute}",
            failure.Value("RouteTemplate")?.ToString());
    }
}
