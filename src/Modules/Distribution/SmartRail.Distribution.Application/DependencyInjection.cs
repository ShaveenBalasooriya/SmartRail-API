using Microsoft.Extensions.DependencyInjection;
using SmartRail.Distribution.Application.Public;
using SmartRail.Distribution.Application.Streams;

namespace SmartRail.Distribution.Application;

/// <summary>
/// The Distribution application layer's registration point. The module's Infrastructure
/// registration calls this; the Host never reaches into the application layer itself.
/// </summary>
public static class DependencyInjection
{
    /// <summary>Registers the application layer's use cases and ports.</summary>
    public static IServiceCollection AddDistributionApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Every public channel reads journeys through the one view service, so one builder
        // produces the item they all return (design D1).
        services.AddScoped<IPublicJourneyViews, PublicJourneyViews>();

        // The live connections outlive any request, so the hub is a singleton and every stream
        // registers through it (design D3).
        services.AddSingleton<IStreamHub, StreamHub>();

        // A resuming connection reads what it missed through Live Operations' contract (design D4).
        services.AddScoped<IStreamCatchUp, StreamCatchUp>();

        return services;
    }
}
