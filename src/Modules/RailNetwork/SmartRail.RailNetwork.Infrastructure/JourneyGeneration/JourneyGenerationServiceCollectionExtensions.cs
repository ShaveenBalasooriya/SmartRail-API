using Microsoft.Extensions.DependencyInjection;

namespace SmartRail.RailNetwork.Infrastructure.JourneyGeneration;

/// <summary>Registers daily journey generation for the Rail Network module (design D2).</summary>
public static class JourneyGenerationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the generator and the hosted service that runs it at start-up and every hour.
    /// <see cref="TimeProvider"/> and the module's <c>DbContext</c> must already be registered.
    /// </summary>
    public static IServiceCollection AddRailNetworkJourneyGeneration(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<JourneyGenerator>();
        services.AddHostedService<JourneyGenerationService>();

        return services;
    }
}
