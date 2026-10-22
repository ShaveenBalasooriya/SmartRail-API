using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SmartRail.Host.Health;

/// <summary>
/// Maps <c>/health/live</c> and <c>/health/ready</c> and writes a body that reveals nothing about
/// the internals: a status, the checks' names and their statuses, and nothing else
/// (service-health spec).
/// </summary>
public static class HealthEndpoints
{
    /// <summary>The liveness route.</summary>
    public const string LivePath = "/health/live";

    /// <summary>The readiness route.</summary>
    public const string ReadyPath = "/health/ready";

    /// <summary>Maps both probes. They need no credentials, so they sit outside the API group.</summary>
    public static WebApplication MapSmartRailHealth(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapHealthChecks(LivePath, new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(HealthChecks.LiveTag),
            ResponseWriter = WriteAsync,
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status200OK,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
            },
        }).AllowAnonymous();

        app.MapHealthChecks(ReadyPath, new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(HealthChecks.ReadyTag),
            ResponseWriter = WriteAsync,
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
            },
        }).AllowAnonymous();

        return app;
    }

    /// <summary>
    /// Writes <c>{ "status": "Healthy", "checks": [ { "name": "...", "status": "..." } ] }</c>.
    /// No description, exception or data is written, so a failing check cannot leak a connection
    /// string, a host name or a credential.
    /// </summary>
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(report);

        context.Response.ContentType = "application/json; charset=utf-8";

        var body = new
        {
            status = report.Status.ToString(),
            checks = report.Entries
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => new
                {
                    name = entry.Key,
                    status = entry.Value.Status.ToString(),
                })
                .ToArray(),
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(body, ApiJson));
    }

    private static readonly JsonSerializerOptions ApiJson = Http.ApiJson.CreateOptions();
}
