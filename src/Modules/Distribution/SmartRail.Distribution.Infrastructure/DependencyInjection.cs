using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartRail.Distribution.Application;

namespace SmartRail.Distribution.Infrastructure;

/// <summary>
/// The Distribution module's Infrastructure registration point. The Host calls this and never
/// reaches into the module's internals itself.
/// </summary>
/// <remarks>
/// Distribution keeps no data, so it has no Domain project and no <c>DbContext</c> (design D1).
/// </remarks>
public static class DependencyInjection
{
    /// <summary>Registers the module's services.</summary>
    public static IServiceCollection AddDistributionInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddDistributionApplication();

        return services;
    }
}
