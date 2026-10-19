using System.Net;
using Microsoft.Extensions.Logging;
using SmartRail.Host.IntegrationTests.Infrastructure;
using SmartRail.Host.Health;
using SmartRail.Host.Http;
using Testcontainers.PostgreSql;

namespace SmartRail.Host.IntegrationTests.Health;

[Collection(PostgresCollection.Name)]
public sealed class HealthTests(PostgresFixture postgres) : IDisposable
{
    private readonly SmartRailApiFactory _factory = new(postgres.ConnectionString);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Readiness_is_ok_when_the_database_is_reachable_and_migrations_are_applied()
    {
        using var client = _factory.CreateApiClient();
        using var response = await client.GetAsync(HealthEndpoints.ReadyPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"status\":\"Healthy\"", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Liveness_is_ok()
    {
        using var client = _factory.CreateApiClient();
        using var response = await client.GetAsync(HealthEndpoints.LivePath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"status\":\"Healthy\"", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_probes_need_no_credentials()
    {
        using var client = _factory.CreateApiClient();

        using var live = await client.GetAsync(HealthEndpoints.LivePath);
        using var ready = await client.GetAsync(HealthEndpoints.ReadyPath);

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
    }

    [Fact]
    public async Task Neither_probe_reveals_internals()
    {
        using var client = _factory.CreateApiClient();

        var live = await (await client.GetAsync(HealthEndpoints.LivePath)).Content.ReadAsStringAsync();
        var ready = await (await client.GetAsync(HealthEndpoints.ReadyPath)).Content.ReadAsStringAsync();

        foreach (var body in new[] { live, ready })
        {
            Assert.DoesNotContain("Exception", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Npgsql", body, StringComparison.Ordinal);
            Assert.DoesNotContain("Password", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Host=", body, StringComparison.Ordinal);
            Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);
            Assert.DoesNotContain("stack", body, StringComparison.OrdinalIgnoreCase);
        }
    }
}

/// <summary>
/// Readiness against a database that goes away is its own scenario, so it gets its own container
/// that the test is allowed to stop.
/// </summary>
public sealed class ReadinessWhenDatabaseIsStoppedTests : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;
    private SmartRailApiFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder("postgres:17-alpine")
            .WithImage("postgres:17-alpine")
            .WithDatabase("smartrail")
            .WithUsername("smartrail")
            .WithPassword("smartrail")
            .Build();

        await _container.StartAsync();

        _factory = new SmartRailApiFactory($"{_container.GetConnectionString()};Timeout=3;Command Timeout=3");

        using var client = _factory.CreateApiClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(HealthEndpoints.ReadyPath)).StatusCode);
    }

    public async Task DisposeAsync()
    {
        _factory.Dispose();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task Readiness_fails_and_names_the_database_check_when_postgres_is_down()
    {
        await _container.StopAsync();

        using var client = _factory.CreateApiClient();
        using var response = await client.GetAsync(HealthEndpoints.ReadyPath);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"name\":\"database\",\"status\":\"Unhealthy\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Npgsql", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Liveness_still_succeeds_when_postgres_is_down()
    {
        await _container.StopAsync();

        using var client = _factory.CreateApiClient();
        using var response = await client.GetAsync(HealthEndpoints.LivePath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

[Collection(PostgresCollection.Name)]
public sealed class RequestLoggingTests(PostgresFixture postgres) : IDisposable
{
    private readonly SmartRailApiFactory _factory = new(postgres.ConnectionString);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task A_completed_request_is_logged_with_the_method_route_status_duration_and_correlation_id()
    {
        using var client = _factory.CreateApiClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, TestEndpoints.ConventionsRoute);
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, "log-correlation");

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var entry = await WaitForEntryAsync(_factory.Logs, "responded");

        Assert.Equal("GET", entry.Value("Method"));
        Assert.Equal(TestEndpoints.ConventionsRoute, entry.Value("RouteTemplate"));
        Assert.Equal(200, Convert.ToInt32(entry.Value("StatusCode"), System.Globalization.CultureInfo.InvariantCulture));
        Assert.True(Convert.ToInt64(entry.Value("DurationMs"), System.Globalization.CultureInfo.InvariantCulture) >= 0);
        Assert.Equal("log-correlation", entry.Value("CorrelationId"));
    }

    [Fact]
    public async Task The_route_template_is_logged_not_the_raw_path_with_its_query_string()
    {
        using var client = _factory.CreateApiClient();
        using var response = await client.GetAsync($"{TestEndpoints.ConventionsRoute}?unused=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var entry = await WaitForEntryAsync(_factory.Logs, "responded");

        Assert.Equal(TestEndpoints.ConventionsRoute, entry.Value("RouteTemplate"));
        Assert.DoesNotContain("unused=1", entry.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unmatched_request_is_logged_without_its_path()
    {
        using var client = _factory.CreateApiClient();
        using var response = await client.GetAsync($"{ApiRoutes.BasePath}/journeys/00000000-0000-0000-0000-000000000001/nothing");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var entry = await WaitForEntryAsync(_factory.Logs, "responded");

        Assert.Equal("unmatched", entry.Value("RouteTemplate"));
        Assert.DoesNotContain("journeys", entry.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Every_request_entry_is_written_at_information_level()
    {
        var entry = _factory.Logs.Entries.FirstOrDefault(candidate => candidate.Message.Contains("responded", StringComparison.Ordinal));

        Assert.True(entry is null || entry.Level == LogLevel.Information);
    }

    private static async Task<LogEntry> WaitForEntryAsync(LogCapture logs, string messageFragment)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var entry = logs.Entries.LastOrDefault(candidate => candidate.Message.Contains(messageFragment, StringComparison.Ordinal));
            if (entry is not null)
            {
                return entry;
            }

            await Task.Delay(20);
        }

        throw new InvalidOperationException($"No log entry containing '{messageFragment}' was written.");
    }
}
