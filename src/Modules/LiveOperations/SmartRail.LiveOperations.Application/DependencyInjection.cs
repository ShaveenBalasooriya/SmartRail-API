using Microsoft.Extensions.DependencyInjection;
using SmartRail.LiveOperations.Application.LiveJourneys;
using SmartRail.LiveOperations.Contracts;

namespace SmartRail.LiveOperations.Application;

/// <summary>
/// The Live Operations application layer's registration point. The module's Infrastructure
/// registration calls this; the Host never reaches into the application layer itself.
/// </summary>
public static class DependencyInjection
{
    /// <summary>Registers the application layer's contract implementation and ports.</summary>
    public static IServiceCollection AddLiveOperationsApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The public read contract is implemented here, by sending the module's own queries.
        services.AddScoped<ILiveJourneys, LiveJourneysContract>();

        return services;
    }
}
