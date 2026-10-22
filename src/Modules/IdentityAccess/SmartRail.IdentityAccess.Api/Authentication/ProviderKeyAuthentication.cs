using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using SmartRail.SharedKernel.Application.Abstractions;

namespace SmartRail.IdentityAccess.Api.Authentication;

/// <summary>
/// Registers the <c>provider</c> scheme and its policy (design D7). The scheme reads a machine
/// client's API key; the policy accepts that scheme only, so a staff token authenticates nothing
/// here and the staff policy accepts no provider key.
/// </summary>
public static class ProviderKeyAuthentication
{
    /// <summary>The scheme and policy name; the two are deliberately the same name (design D4).</summary>
    public const string Scheme = "provider";

    /// <summary>The header a provider sends its key in; never a query string, so it stays out of logs.</summary>
    public const string HeaderName = "X-Api-Key";

    /// <summary>
    /// The one message every rejected key produces. It never carries the key or says which part was
    /// wrong, so an unknown prefix and a wrong secret are indistinguishable.
    /// </summary>
    public const string FailureMessage = "The API key is missing or invalid.";

    /// <summary>Adds the provider-key authentication handler and the policy it satisfies.</summary>
    public static IServiceCollection AddProviderKeyAuthentication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services
            .AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, ProviderKeyAuthenticationHandler>(Scheme, null);

        services.AddAuthorization(options => options.AddPolicy(
            Policies.Provider,
            new AuthorizationPolicyBuilder(Scheme)
                .RequireAuthenticatedUser()
                .RequireClaim(SmartRailClaims.ProviderId)
                .Build()));

        return services;
    }
}
