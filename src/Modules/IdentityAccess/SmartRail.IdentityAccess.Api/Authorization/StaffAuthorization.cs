using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartRail.IdentityAccess.Api.Authentication;
using SmartRail.SharedKernel.Application.Abstractions;

namespace SmartRail.IdentityAccess.Api.Authorization;

/// <summary>
/// Registers the staff fallback policy and the handler that announces denied writes (design D4, D6).
/// </summary>
public static class StaffAuthorization
{
    /// <summary>
    /// An endpoint that declares no access policy still requires an authenticated staff caller with
    /// a SmartRail account, so forgetting a classification closes the endpoint instead of opening it.
    /// Health, documentation and the explicitly public endpoints opt out with <c>AllowAnonymous</c>.
    /// </summary>
    public static IServiceCollection AddStaffAuthorization(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder(StaffAuthentication.Scheme)
                .RequireAuthenticatedUser()
                .RequireClaim(SmartRailClaims.StaffId)
                .Build();
        });

        // The default handler stays the one that writes the response; ours announces first.
        services.TryAddSingleton<AuthorizationMiddlewareResultHandler>();
        services.RemoveAll<IAuthorizationMiddlewareResultHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, AccessDeniedAuthorizationResultHandler>();

        return services;
    }
}
