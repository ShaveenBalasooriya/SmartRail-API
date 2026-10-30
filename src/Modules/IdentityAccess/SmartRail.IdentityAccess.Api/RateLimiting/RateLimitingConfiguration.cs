using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartRail.SharedKernel.Application.Abstractions;

namespace SmartRail.IdentityAccess.Api.RateLimiting;

/// <summary>
/// The two fixed-window policies every module's endpoints share (design D8). Staff and health
/// endpoints carry no policy, so operations are never throttled during a disruption.
/// </summary>
public static class RateLimitingConfiguration
{
    /// <summary>The public limit per client per minute; the spec's default is 120.</summary>
    public const string PublicPermitLimitSetting = "RateLimiting:Public:PermitLimit";

    /// <summary>The provider limit per key per minute; the spec's default is 60.</summary>
    public const string ProviderPermitLimitSetting = "RateLimiting:Provider:PermitLimit";

    private const int DefaultPublicPermitLimit = 120;
    private const int DefaultProviderPermitLimit = 60;

    /// <summary>
    /// Registers both policies. A rejection is a problem-details 429 with <c>Retry-After</c>, so the
    /// client learns when to come back (api-rate-limiting spec).
    /// </summary>
    public static IServiceCollection AddSmartRailRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var publicPermitLimit = configuration.GetValue(PublicPermitLimitSetting, DefaultPublicPermitLimit);
        var providerPermitLimit = configuration.GetValue(ProviderPermitLimitSetting, DefaultProviderPermitLimit);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // The public permit is acquired once, when the request starts, and held for its
            // lifetime: a long-lived SSE stream therefore counts once per connection and its
            // events never count (live-event-streams spec, "Live streams count once per
            // connection").
            options.AddPolicy(Policies.PublicRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
                PartitionKey(context.Connection.RemoteIpAddress?.ToString()),
                _ => Window(publicPermitLimit)));

            options.AddPolicy(Policies.ProviderRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
                PartitionKey(
                    context.User.FindFirst(SmartRailClaims.ProviderId)?.Value
                    ?? context.Connection.RemoteIpAddress?.ToString()),
                _ => Window(providerPermitLimit)));

            options.OnRejected = WriteRejectionAsync;
        });

        return services;
    }

    /// <summary>
    /// The remote address is held in memory only and never logged; a request without one shares a
    /// single partition rather than escaping the limit.
    /// </summary>
    private static string PartitionKey(string? key) => string.IsNullOrEmpty(key) ? "unknown" : key;

    private static FixedWindowRateLimiterOptions Window(int permitLimit) => new()
    {
        PermitLimit = permitLimit,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
        AutoReplenishment = true,
    };

    private static async ValueTask WriteRejectionAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var response = context.HttpContext.Response;
        response.StatusCode = StatusCodes.Status429TooManyRequests;

        response.Headers.RetryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
            ? Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture)
            : "60";

        var problemDetails = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();

        await problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context.HttpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Too many requests.",
            },
        });
    }
}
