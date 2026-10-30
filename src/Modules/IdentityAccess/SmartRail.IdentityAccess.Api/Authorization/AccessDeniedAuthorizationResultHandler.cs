using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SmartRail.IdentityAccess.Application.Abstractions;
using SmartRail.IdentityAccess.Contracts;
using SmartRail.SharedKernel.Application.Abstractions;
using SmartRail.SharedKernel.Application.Events;

namespace SmartRail.IdentityAccess.Api.Authorization;

/// <summary>
/// Publishes every 403 on a state-changing endpoint as an <see cref="AccessDenied"/> through the
/// module's outbox, then lets the default handler write the response (design D6, D9). A denied read
/// is silent, because only attempts to change state are audited.
/// </summary>
internal sealed class AccessDeniedAuthorizationResultHandler(
    AuthorizationMiddlewareResultHandler defaultHandler,
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
            // This handler is a singleton while the publisher and the unit of work are scoped to
            // the request, so both are resolved from the request on every call (design D9).
            var publisher = context.RequestServices.GetRequiredKeyedService<IIntegrationEventPublisher>(
                IntegrationEventPublisherKeys.Identity);
            var unitOfWork = context.RequestServices.GetRequiredService<IUnitOfWork>();

            // One identifier for the payload and the outbox row, so a consumer can recognise a
            // redelivery of this denial even though the bus hands it only the payload (audit D5).
            var eventId = Guid.CreateVersion7();

            publisher.Publish(
                new AccessDenied(
                    eventId,
                    StaffIdOf(context.User),
                    context.User.FindFirstValue(SmartRailClaims.Role),
                    ActionOf(context),
                    timeProvider.GetUtcNow()),
                eventId);

            // A denied request has no business transaction of its own, so its outbox row is saved
            // here; the dispatcher delivers it only once this commits (design D9).
            await unitOfWork.SaveChangesAsync();
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
