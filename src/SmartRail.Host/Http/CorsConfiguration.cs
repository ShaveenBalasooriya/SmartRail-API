namespace SmartRail.Host.Http;

/// <summary>
/// CORS is allow-list only: a browser request from an origin that is not configured gets no
/// <c>Access-Control-Allow-Origin</c> header, so the browser refuses the response (api-conventions
/// spec).
/// </summary>
public static class CorsConfiguration
{
    /// <summary>The name of the policy every module's endpoints use.</summary>
    public const string PolicyName = "smartrail-clients";

    /// <summary>The configuration section holding the allowed origins.</summary>
    public const string SectionName = "Cors";

    /// <summary>
    /// Registers the allow-list policy. The origins come from <c>Cors:AllowedOrigins</c>; when the
    /// list is empty no origin is allowed, which is the safe default.
    /// </summary>
    public static IServiceCollection AddSmartRailCors(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var origins = configuration.GetSection($"{SectionName}:AllowedOrigins").Get<string[]>() ?? [];

        services.AddCors(options => options.AddPolicy(PolicyName, policy =>
        {
            if (origins.Length == 0)
            {
                // No configured origin means no cross-origin browser request is allowed. Calling
                // WithOrigins() with an empty list would throw, so the policy stays empty instead.
                policy.WithOrigins("https://no-origin-configured.invalid");
                return;
            }

            policy
                .WithOrigins(origins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .WithExposedHeaders(CorrelationIdMiddleware.HeaderName)
                .SetPreflightMaxAge(TimeSpan.FromMinutes(10));
        }));

        return services;
    }
}
