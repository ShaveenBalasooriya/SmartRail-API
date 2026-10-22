using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartRail.RailNetwork.Application.Clock;

namespace SmartRail.RailNetwork.Application;

/// <summary>
/// The RailNetwork application layer's registration point. The module's Infrastructure
/// registration calls this; the Host never reaches into the application layer itself.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers the application services. <see cref="TimeProvider"/> must already be registered;
    /// the Host registers the system clock.
    /// </summary>
    public static IServiceCollection AddRailNetworkApplication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var configuredTimeZoneId = configuration["Railway:TimeZone"];
        var timeZoneId = string.IsNullOrWhiteSpace(configuredTimeZoneId)
            ? RailwayClock.DefaultTimeZoneId
            : configuredTimeZoneId;

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);

        services.AddSingleton(provider => new RailwayClock(
            provider.GetRequiredService<TimeProvider>(),
            timeZone));

        return services;
    }
}
