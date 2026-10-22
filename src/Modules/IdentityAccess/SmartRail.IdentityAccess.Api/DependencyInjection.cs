using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartRail.IdentityAccess.Api.Authentication;
using SmartRail.IdentityAccess.Api.Authorization;
using SmartRail.IdentityAccess.Api.Claims;
using SmartRail.IdentityAccess.Api.CurrentUser;
using SmartRail.IdentityAccess.Api.RateLimiting;
using SmartRail.SharedKernel.Application.Abstractions;

namespace SmartRail.IdentityAccess.Api;

/// <summary>
/// The Identity &amp; Access API layer's registration point. The Host calls this and then maps
/// every Carter module it finds in this assembly under <c>/api/v1</c>.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers staff and provider authentication, the fallback policy, the access-denied handler,
    /// the rate-limit policies and the <see cref="ICurrentUser"/> port.
    /// </summary>
    public static IServiceCollection AddIdentityAccessApi(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services.AddStaffAuthentication(configuration, environment);
        services.AddStaffAuthorization();
        services.AddProviderKeyAuthentication();
        services.AddSmartRailRateLimiting(configuration);

        services.AddHttpContextAccessor();
        services.AddTransient<IClaimsTransformation, StaffClaimsTransformation>();
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

        return services;
    }
}
