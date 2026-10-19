using Microsoft.AspNetCore.Http.Features;

namespace SmartRail.Host.Http;

/// <summary>
/// The named route groups every module maps its endpoints into. Keeping the prefix here means the
/// <c>/api/v1</c> base path is decided once (api-conventions spec, design D4).
/// </summary>
public static class ApiRoutes
{
    /// <summary>The versioned base path for every application endpoint.</summary>
    public const string BasePath = "/api/v1";

    /// <summary>The base path for health probes, which are outside <c>/api/v1</c>.</summary>
    public const string HealthPath = "/health";

    /// <summary>Creates the versioned group that a module adds its area group to.</summary>
    public static RouteGroupBuilder MapApiV1(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGroup(BasePath);
}

/// <summary>
/// A set of endpoints mapped into the application after the module endpoints. It is resolved from
/// the container, which is what lets the integration tests add their own endpoints through
/// <c>ConfigureTestServices</c> without any test code living in the Host.
/// </summary>
public interface IEndpointRegistration
{
    /// <summary>Maps this registration's endpoints onto the application.</summary>
    void Map(IEndpointRouteBuilder endpoints);
}
