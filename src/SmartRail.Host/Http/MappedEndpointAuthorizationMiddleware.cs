using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace SmartRail.Host.Http;

/// <summary>
/// Keeps the authorisation fallback policy to the endpoints the application maps (design D4).
/// </summary>
/// <remarks>
/// .NET 10's authorisation middleware uses the fallback policy for every request that carries no
/// policy of its own, including the answers routing produces itself: an unmatched path, and the
/// framework's 404/405/415 rejection endpoints, which carry no metadata at all. Those are not
/// application endpoints someone forgot to classify, so they must keep their normal answers rather
/// than being replaced with a challenge. An unmatched path is answered 404 here; a rejection
/// endpoint is marked anonymous so routing can answer 405 or 415.
/// </remarks>
internal sealed class MappedEndpointAuthorizationMiddleware(RequestDelegate next)
{
    /// <summary>The display name of routing's 405 rejection endpoint.</summary>
    private const string MethodNotAllowedEndpoint = "405 HTTP Method Not Supported";

    /// <summary>The display name of routing's 415 rejection endpoint.</summary>
    private const string UnsupportedMediaTypeEndpoint = "415 HTTP Unsupported Media Type";

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpoint = context.GetEndpoint();

        if (endpoint is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (IsRoutingRejection(endpoint))
        {
            context.SetEndpoint(new Endpoint(
                endpoint.RequestDelegate,
                new EndpointMetadataCollection(new AllowAnonymousAttribute()),
                endpoint.DisplayName));
        }

        await next(context);
    }

    private static bool IsRoutingRejection(Endpoint endpoint) =>
        endpoint.DisplayName is MethodNotAllowedEndpoint or UnsupportedMediaTypeEndpoint;
}
