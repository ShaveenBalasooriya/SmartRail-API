using Microsoft.Extensions.DependencyInjection;

namespace SmartRail.LiveOperations.Application;

/// <summary>
/// The Live Operations application layer's registration point. The module's Infrastructure registration
/// calls this; the Host never reaches into the application layer itself.
/// </summary>
public static class DependencyInjection
{
    /// <summary>Registers the application layer's use cases and ports.</summary>
    public static IServiceCollection AddLiveOperationsApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services;
    }
}
