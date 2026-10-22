using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using SmartRail.SharedKernel.Application.Abstractions;

namespace SmartRail.SharedKernel.Api.Authorization;

/// <summary>
/// Marks an endpoint as public: anonymous callers are served and the public rate limit applies
/// (design D4, D8). The marker is what the OpenAPI transformers read later.
/// </summary>
public sealed record PublicAccessMetadata;

/// <summary>Marks a staff endpoint and names the roles it accepts.</summary>
/// <param name="Roles">The catalogue roles the endpoint allows.</param>
public sealed record StaffAccessMetadata(IReadOnlyList<string> Roles);

/// <summary>Marks an endpoint as provider-only: a valid API key and the provider rate limit.</summary>
public sealed record ProviderAccessMetadata;

/// <summary>
/// The three ways a module classifies an endpoint (design D4). The fallback policy closes anything
/// that declares no classification, so forgetting a helper is a refusal, never an open endpoint.
/// </summary>
public static class EndpointAccessExtensions
{
    /// <summary>The schemes and policies are both named <c>staff</c>; the token scheme is the same name.</summary>
    private const string StaffScheme = "staff";

    /// <summary>
    /// Serves the endpoint anonymously, applies the public rate limit and records that it is
    /// public. An <c>Authorization</c> header sent to it is ignored.
    /// </summary>
    public static TBuilder AllowAnonymousPublic<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AllowAnonymous();
        builder.RequireRateLimiting(Policies.PublicRateLimit);
        builder.WithMetadata(new PublicAccessMetadata());

        return builder;
    }

    /// <summary>
    /// Allows only an authenticated staff caller with a SmartRail account whose resolved role is one
    /// of <paramref name="roles"/>. The policy checks the single resolved role claim, never the raw
    /// <c>groups</c> claim, so a token naming two role groups satisfies neither (design D1, D2).
    /// </summary>
    public static TBuilder RequireStaffRole<TBuilder>(this TBuilder builder, params string[] roles)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(roles);

        var policy = new AuthorizationPolicyBuilder(StaffScheme)
            .RequireAuthenticatedUser()
            .RequireClaim(SmartRailClaims.StaffId)
            .RequireClaim(SmartRailClaims.Role, roles)
            .Build();

        builder.RequireAuthorization(policy);
        builder.WithMetadata(new StaffAccessMetadata(roles));

        return builder;
    }

    /// <summary>
    /// Allows only a valid provider API key and applies the provider rate limit. The provider
    /// scheme and policy are registered with the provider-key work; no endpoint uses this yet.
    /// </summary>
    public static TBuilder RequireProvider<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.RequireAuthorization(Policies.Provider);
        builder.RequireRateLimiting(Policies.ProviderRateLimit);
        builder.WithMetadata(new ProviderAccessMetadata());

        return builder;
    }
}
