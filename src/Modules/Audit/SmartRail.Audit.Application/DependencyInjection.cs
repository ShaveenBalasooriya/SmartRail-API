using Microsoft.Extensions.DependencyInjection;

namespace SmartRail.Audit.Application;

/// <summary>
/// The Audit application layer's registration point. The module's Infrastructure registration
/// calls this; the Host never reaches into the application layer itself.
/// </summary>
public static class DependencyInjection
{
    /// <summary>Registers the application layer's use cases and ports.</summary>
    public static IServiceCollection AddAuditApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services;
    }
}
