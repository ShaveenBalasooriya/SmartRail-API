using Microsoft.Extensions.DependencyInjection;
using SmartRail.IdentityAccess.Application.Staff;
using SmartRail.IdentityAccess.Contracts;

namespace SmartRail.IdentityAccess.Application;

/// <summary>
/// The Identity &amp; Access application layer's registration point. The module's Infrastructure
/// registration calls this; the Host never reaches into the application layer itself.
/// </summary>
public static class DependencyInjection
{
    /// <summary>Registers the application layer's use cases and ports.</summary>
    public static IServiceCollection AddIdentityAccessApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IStaffDirectory, StaffDirectoryService>();

        return services;
    }
}
