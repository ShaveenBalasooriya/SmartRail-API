using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SmartRail.IdentityAccess.Contracts;
using SmartRail.SharedKernel.Application.Abstractions;

namespace SmartRail.IdentityAccess.Api.Authorization;

/// <summary>
/// Announces every 403 on a state-changing endpoint as an <see cref="AccessDenied"/> notification,
/// then lets the default handler write the response (design D6). A denied read is silent, because
/// only attempts to change state are audited.
/// </summary>
internal sealed class AccessDeniedAuthorizationResultHandler(
    AuthorizationMiddlewareResultHandler defaultHandler,
    IPublisher publisher,
    TimeProvider timeProvider) : IAuthorizationMiddlewareResultHandler
{
    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(authorizeResult);

        if (authorizeResult.Forbidden && IsStateChanging(context.Request.Method))
        {
            await publisher.Publish(new AccessDenied(
                StaffIdOf(context.User),
                context.User.FindFirstValue(SmartRailClaims.Role),
                ActionOf(context),
                timeProvider.GetUtcNow()));
        }

        await defaultHandler.HandleAsync(next, context, policy, authorizeResult);
    }

    private static bool IsStateChanging(string method) =>
        HttpMethods.IsPost(method)
        || HttpMethods.IsPut(method)
        || HttpMethods.IsPatch(method)
        || HttpMethods.IsDelete(method);

    private static Guid? StaffIdOf(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(SmartRailClaims.StaffId), out var staffId) ? staffId : null;

    /// <summary>The endpoint's name, or the method and route template when it has none.</summary>
    private static string ActionOf(HttpContext context)
    {
        var endpoint = context.GetEndpoint();

        if (endpoint?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName is { Length: > 0 } name)
        {
            return name;
        }

        var route = (endpoint as RouteEndpoint)?.RoutePattern.RawText ?? context.Request.Path.Value ?? string.Empty;

        return $"{context.Request.Method} {route}";
    }
}
