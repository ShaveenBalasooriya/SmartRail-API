using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SmartRail.Distribution.Infrastructure;

/// <summary>
/// The Distribution module's registration point. The Host calls this and never reaches into the
/// module's internals itself.
/// </summary>
/// <remarks>
/// Distribution keeps no data, so it has no Domain project and no <c>DbContext</c> (design D1).
/// </remarks>
public static class DistributionModule
{
    /// <summary>The module's name, used for logging and the API tag.</summary>
    public const string ModuleName = "distribution";

    /// <summary>Registers the module's services.</summary>
    public static IServiceCollection AddDistributionModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        return services;
    }
}
