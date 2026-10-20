using Microsoft.Extensions.DependencyInjection;
using SmartRail.RailNetwork.Contracts;

namespace SmartRail.RailNetwork.Infrastructure.Queries;

/// <summary>
/// Registers the module's in-process read contracts (design D4). The application layer, and with it
/// the <see cref="Application.Clock.RailwayClock"/>, is registered by the module itself.
/// </summary>
public static class QueryServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="INetworkCatalog"/> and <see cref="ITimetable"/>. The module's
    /// <c>DbContext</c> and <see cref="Application.Clock.RailwayClock"/> must already be registered.
    /// </summary>
    public static IServiceCollection AddRailNetworkQueries(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<INetworkCatalog, NetworkCatalogQueries>();
        services.AddScoped<ITimetable, TimetableQueries>();

        return services;
    }
}
