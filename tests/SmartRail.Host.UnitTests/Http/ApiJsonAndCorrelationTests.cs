using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using SmartRail.Host.Http;

namespace SmartRail.Host.UnitTests.Http;

public sealed class ApiJsonTests
{
    private static readonly JsonSerializerOptions Options = ApiJson.CreateOptions();

    [Fact]
    public void Property_names_are_camel_case_and_enums_are_snake_case()
    {
        var json = JsonSerializer.Serialize(
            new { lifecycleStatus = LifecycleStatus.Cancelled, lastUpdated = DateTimeOffset.UnixEpoch },
            Options);

        Assert.Contains("\"lifecycleStatus\":\"cancelled\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void A_utc_timestamp_is_written_as_an_iso_8601_instant_ending_in_z()
    {
        var json = JsonSerializer.Serialize(new { at = new DateTimeOffset(2026, 10, 19, 2, 45, 0, TimeSpan.Zero) }, Options);

        Assert.Contains("\"at\":\"2026-10-19T02:45:00Z\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void A_timestamp_with_an_offset_is_converted_to_utc_when_written()
    {
        var json = JsonSerializer.Serialize(
            new { at = new DateTimeOffset(2026, 10, 19, 8, 15, 0, TimeSpan.FromHours(5.5)) },
            Options);

        Assert.Contains("\"at\":\"2026-10-19T02:45:00Z\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void A_timestamp_with_an_offset_is_accepted_and_normalised()
    {
        var value = JsonSerializer.Deserialize<DateTimeOffset>("\"2026-10-19T08:15:00+05:30\"", Options);

        Assert.Equal(
            new DateTimeOffset(2026, 10, 19, 2, 45, 0, TimeSpan.Zero),
            value.ToUniversalTime());
    }

    [Fact]
    public void A_timestamp_without_an_offset_is_rejected()
    {
        var exception = Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<DateTimeOffset>("\"2026-10-19T08:15:00\"", Options));

        Assert.Contains("offset", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("2026-10-19T02:45:00Z", true)]
    [InlineData("2026-10-19T08:15:00+05:30", true)]
    [InlineData("2026-10-19T08:15:00-03:00", true)]
    [InlineData("2026-10-19T08:15:00", false)]
    [InlineData("2026-10-19", false)]
    [InlineData("2026-10-19T08:15:00.123", false)]
    public void Offset_detection_matches_the_spec(string text, bool expected)
    {
        Assert.Equal(expected, UtcDateTimeOffsetConverter.HasOffset(text));
    }

    private enum LifecycleStatus
    {
        Scheduled,
        Running,
        Cancelled,
        Completed,
    }
}

public sealed class CorrelationIdTests
{
    [Fact]
    public void A_well_formed_caller_id_is_echoed()
    {
        Assert.Equal("abc-123", CorrelationIdMiddleware.Resolve("abc-123"));
    }

    [Theory]
    [InlineData("has spaces")]
    [InlineData("bad!chars")]
    [InlineData("")]
    public void A_malformed_caller_id_is_replaced(string supplied)
    {
        var resolved = CorrelationIdMiddleware.Resolve(supplied);

        Assert.NotEqual(supplied, resolved);
        Assert.True(Guid.TryParse(resolved, out _));
    }

    [Fact]
    public void An_id_longer_than_the_limit_is_replaced()
    {
        var tooLong = new string('a', CorrelationIdMiddleware.MaxLength + 1);

        var resolved = CorrelationIdMiddleware.Resolve(tooLong);

        Assert.NotEqual(tooLong, resolved);
    }

    [Fact]
    public void An_id_exactly_at_the_limit_is_accepted()
    {
        var atLimit = new string('a', CorrelationIdMiddleware.MaxLength);

        Assert.Equal(atLimit, CorrelationIdMiddleware.Resolve(atLimit));
    }

    [Fact]
    public void No_identifier_at_all_generates_one()
    {
        var resolved = CorrelationIdMiddleware.Resolve(default);

        Assert.True(Guid.TryParse(resolved, out _));
    }
}

public sealed class ApiRouteTests
{
    [Fact]
    public void The_api_base_path_is_versioned()
    {
        Assert.Equal("/api/v1", ApiRoutes.BasePath);
        Assert.Equal("/health", ApiRoutes.HealthPath);
    }

    [Fact]
    public void The_health_paths_are_outside_the_api_base_path()
    {
        Assert.StartsWith(ApiRoutes.HealthPath, SmartRail.Host.Health.HealthEndpoints.LivePath, StringComparison.Ordinal);
        Assert.StartsWith(ApiRoutes.HealthPath, SmartRail.Host.Health.HealthEndpoints.ReadyPath, StringComparison.Ordinal);
    }
}

public sealed class DocumentationEnvironmentTests
{
    [Theory]
    [InlineData("Development", true)]
    [InlineData("E2E", true)]
    [InlineData("Production", false)]
    [InlineData("Staging", false)]
    public void Documentation_defaults_by_environment(string environmentName, bool expected)
    {
        var environment = new ProbeEnvironment(environmentName);
        var configuration = new ConfigurationBuilder().Build();

        Assert.Equal(expected, OpenApiConfiguration.IsEnabled(environment, configuration));
    }

    [Theory]
    [InlineData("Production", "true", true)]
    [InlineData("Development", "false", false)]
    public void Configuration_overrides_the_environment_default(string environmentName, string configured, bool expected)
    {
        var environment = new ProbeEnvironment(environmentName);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [OpenApiConfiguration.EnabledSetting] = configured })
            .Build();

        Assert.Equal(expected, OpenApiConfiguration.IsEnabled(environment, configuration));
    }

    private sealed class ProbeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "SmartRail.Host";

        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
