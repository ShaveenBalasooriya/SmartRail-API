using System.Net;
using SmartRail.Host.IntegrationTests.Infrastructure;
using SmartRail.Host.Http;

namespace SmartRail.Host.IntegrationTests.Conventions;

[Collection(PostgresCollection.Name)]
public sealed class CorrelationIdTests(PostgresFixture postgres) : IDisposable
{
    private readonly SmartRailApiFactory _factory = new(postgres.ConnectionString);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task A_well_formed_caller_id_is_echoed_and_appears_in_that_request_s_log_entries()
    {
        using var client = _factory.CreateApiClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, TestEndpoints.ConventionsRoute);
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, "abc-123");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "abc-123",
            response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single());

        await WaitForRequestLogAsync(_factory.Logs);
        var entry = _factory.Logs.Entries.First(entry =>
            entry.Message.Contains("responded", StringComparison.Ordinal)
            && entry.Message.Contains("abc-123", StringComparison.Ordinal));

        Assert.Equal("abc-123", entry.Value("CorrelationId"));
    }

    [Fact]
    public async Task A_request_with_no_id_gets_a_generated_one()
    {
        using var client = _factory.CreateApiClient();
        using var response = await client.GetAsync(TestEndpoints.ConventionsRoute);

        var correlationId = response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single();

        Assert.True(Guid.TryParse(correlationId, out _), $"'{correlationId}' is not a generated identifier");
    }

    [Theory]
    [InlineData("has spaces and is therefore invalid")]
    [InlineData("bad!characters!here")]
    public async Task An_invalid_caller_id_is_ignored_and_replaced(string invalid)
    {
        using var client = _factory.CreateApiClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, TestEndpoints.ConventionsRoute);
        request.Headers.TryAddWithoutValidation(CorrelationIdMiddleware.HeaderName, invalid);

        using var response = await client.SendAsync(request);

        var correlationId = response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single();
        Assert.NotEqual(invalid, correlationId);
        Assert.True(Guid.TryParse(correlationId, out _));
    }

    [Fact]
    public async Task An_over_long_caller_id_is_ignored_and_replaced()
    {
        using var client = _factory.CreateApiClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, TestEndpoints.ConventionsRoute);
        request.Headers.TryAddWithoutValidation(
            CorrelationIdMiddleware.HeaderName,
            new string('a', CorrelationIdMiddleware.MaxLength + 1));

        using var response = await client.SendAsync(request);

        var correlationId = response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single();
        // A generated ID is the "D" form of a GUID: 36 characters, which is well within the
        // accepted maximum of 64 (api-conventions spec).
        Assert.Equal(36, correlationId.Length);
        Assert.True(Guid.TryParse(correlationId, out _));
    }

    [Fact]
    public async Task An_error_response_also_carries_the_correlation_id_header()
    {
        using var client = _factory.CreateApiClient();
        using var response = await client.GetAsync($"{ApiRoutes.BasePath}/no-such-endpoint");

        Assert.False(string.IsNullOrWhiteSpace(
            response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).SingleOrDefault()));
    }

    /// <summary>The request log is written after the response, so allow it a moment to land.</summary>
    private static async Task WaitForRequestLogAsync(LogCapture logs)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (logs.Entries.Any(entry => entry.Message.Contains("responded", StringComparison.Ordinal)))
            {
                return;
            }

            await Task.Delay(20);
        }
    }
}

[Collection(PostgresCollection.Name)]
public sealed class CorsTests(PostgresFixture postgres) : IDisposable
{
    private const string AllowedOrigin = "https://ops.smartrail.test";
    private const string UnlistedOrigin = "https://not-configured.example";

    private readonly SmartRailApiFactory _factory = new(postgres.ConnectionString);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task An_unlisted_origin_receives_no_allow_origin_header()
    {
        using var client = _factory.CreateApiClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, TestEndpoints.ConventionsRoute);
        request.Headers.Add("Origin", UnlistedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "GET");

        using var response = await client.SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task A_configured_origin_is_allowed()
    {
        using var client = _factory.CreateApiClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, TestEndpoints.ConventionsRoute);
        request.Headers.Add("Origin", AllowedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "GET");

        using var response = await client.SendAsync(request);

        Assert.Equal(
            AllowedOrigin,
            response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public async Task A_simple_request_from_an_unlisted_origin_gets_no_allow_origin_header()
    {
        using var client = _factory.CreateApiClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, TestEndpoints.ConventionsRoute);
        request.Headers.Add("Origin", UnlistedOrigin);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }
}
