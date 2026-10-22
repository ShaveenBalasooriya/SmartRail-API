using Microsoft.Extensions.DependencyInjection;
using SmartRail.RailNetwork.Application.Network;
using SmartRail.RailNetwork.Application.Timetable;
using SmartRail.RailNetwork.Contracts;

namespace SmartRail.RailNetwork.Application;

/// <summary>
/// The Rail Network application layer's registration point. The Host never reaches into the
/// application layer itself; the module's Infrastructure registration calls this.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers the implementations of the module's public contracts. Handlers and validators are
    /// registered once by the Host, over this assembly (design D2).
    /// </summary>
    public static IServiceCollection AddRailNetworkApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<INetworkCatalog, NetworkCatalogService>();
        services.AddScoped<ITimetable, TimetableService>();

        return services;
    }
}
