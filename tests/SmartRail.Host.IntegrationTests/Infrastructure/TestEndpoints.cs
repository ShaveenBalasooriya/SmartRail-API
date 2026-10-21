using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using SmartRail.Host.Http;
using SmartRail.SharedKernel.Api.Authorization;
using SmartRail.SharedKernel.Application.Abstractions;

namespace SmartRail.Host.IntegrationTests.Infrastructure;

/// <summary>
/// Endpoints that exist only while the integration tests run. They let the shared HTTP contract be
/// asserted without inventing production endpoints: an enumeration and a timestamp to prove the
/// JSON rules, a validating body to prove the 400 shape, a throwing handler to prove the 500 shape,
/// three access-policy endpoints to prove the staff policy, the fallback and the denial event, and
/// a provider endpoint to prove the API-key scheme.
/// They are registered through <see cref="IEndpointRegistration"/>, so they are mapped by the Host
/// but defined in the tests.
/// </summary>
public sealed class TestEndpoints : IEndpointRegistration
{
    /// <summary>The group prefix every test endpoint sits under.</summary>
    public const string Prefix = "/_test";

    /// <summary>The route of the endpoint that throws.</summary>
    public const string FailureRoute = $"{ApiRoutes.BasePath}{Prefix}/failure";

    /// <summary>The route of the validating endpoint.</summary>
    public const string ValidationRoute = $"{ApiRoutes.BasePath}{Prefix}/validation";

    /// <summary>The route of the timestamp endpoint.</summary>
    public const string TimestampRoute = $"{ApiRoutes.BasePath}{Prefix}/timestamp";

    /// <summary>The route of the JSON-conventions endpoint.</summary>
    public const string ConventionsRoute = $"{ApiRoutes.BasePath}{Prefix}/conventions";

    /// <summary>A POST only the Network Controller may call, to prove a denied write is announced.</summary>
    public const string ControllerWriteRoute = $"{ApiRoutes.BasePath}{Prefix}/controller-write";

    /// <summary>A GET only the Network Controller may call, to prove a denied read is silent.</summary>
    public const string ControllerReadRoute = $"{ApiRoutes.BasePath}{Prefix}/controller-read";

    /// <summary>A route with no access policy, which the fallback must therefore close.</summary>
    public const string UnclassifiedRoute = $"{ApiRoutes.BasePath}{Prefix}/unclassified";

    /// <summary>A provider-only route, which echoes the authenticated provider's ID.</summary>
    public const string ProviderRoute = $"{ApiRoutes.BasePath}{Prefix}/provider";

    /// <summary>The text the throwing endpoint includes in its exception, which must never escape.</summary>
    public const string FailureSecret = "internal identifier 8f2c0d1e must not leak";

    public void Map(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints
            .MapGroup($"{ApiRoutes.BasePath}{Prefix}")
            .WithTags("Test")
            .AllowAnonymous();

        group.MapGet("/conventions", () => Results.Ok(new ConventionsResponse(
            LifecycleStatus.Cancelled,
            new DateTimeOffset(2026, 10, 19, 2, 45, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 19, 8, 15, 0, TimeSpan.FromHours(5.5)))))
            .WithSummary("Returns an enumeration and timestamps for the JSON contract test.")
            .WithDescription("Test-only. Access: none.")
            .Produces<ConventionsResponse>();

        group.MapPost("/validation", (ValidationRequest request) => Results.Ok(request))
            .WithSummary("Accepts a body that must satisfy two rules.")
            .WithDescription("Test-only. Access: none.")
            .ProducesValidationProblem();

        group.MapPost("/timestamp", (TimestampRequest request) => Results.Ok(request))
            .WithSummary("Accepts a timestamp that must carry an offset.")
            .WithDescription("Test-only. Access: none.")
            .Produces<TimestampRequest>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/failure", IResult () => throw new InvalidOperationException(FailureSecret))
            .WithSummary("Throws, to prove the error contract.")
            .WithDescription("Test-only. Access: none.")
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // These sit outside the anonymous group: an AllowAnonymous marker on a group would win over
        // a staff policy on its children, so the access-policy endpoints are mapped on the root.
        endpoints.MapPost($"{ApiRoutes.BasePath}{Prefix}/controller-write", () => Results.Ok())
            .WithName("TestControllerOnlyWrite")
            .WithSummary("A write only the Network Controller may perform.")
            .WithDescription("Test-only. Access: staff, network_controller.")
            .RequireStaffRole(Roles.NetworkController);

        endpoints.MapGet($"{ApiRoutes.BasePath}{Prefix}/controller-read", () => Results.Ok())
            .WithName("TestControllerOnlyRead")
            .WithSummary("A read only the Network Controller may perform.")
            .WithDescription("Test-only. Access: staff, network_controller.")
            .RequireStaffRole(Roles.NetworkController);

        endpoints.MapGet($"{ApiRoutes.BasePath}{Prefix}/unclassified", () => Results.Ok())
            .WithName("TestUnclassified")
            .WithSummary("Declares no access policy, so the fallback policy applies.")
            .WithDescription("Test-only. Access: none declared.");

        endpoints.MapGet($"{ApiRoutes.BasePath}{Prefix}/provider", (ClaimsPrincipal user) =>
                Results.Ok(new ProviderIdentityResponse(
                    user.FindFirstValue(SmartRailClaims.ProviderId) ?? string.Empty)))
            .WithName("TestProviderOnly")
            .WithSummary("A provider-only endpoint that echoes the authenticated provider's ID.")
            .WithDescription("Test-only. Access: provider API key.")
            .Produces<ProviderIdentityResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .RequireProvider();
    }
}

/// <summary>The provider the API key identified, as the provider test endpoint returns it.</summary>
public sealed record ProviderIdentityResponse(string ProviderId);

/// <summary>The payload of the JSON-conventions test endpoint.</summary>
public sealed record ConventionsResponse(
    LifecycleStatus LifecycleStatus,
    DateTimeOffset UtcTimestamp,
    DateTimeOffset OffsetTimestamp);

/// <summary>A journey lifecycle status, used to prove enum serialisation.</summary>
public enum LifecycleStatus
{
    Scheduled,
    Running,
    Cancelled,
    Completed,
}

/// <summary>A body with two rules, so one request can fail twice.</summary>
public sealed record ValidationRequest
{
    [Required]
    [MinLength(3)]
    public string Name { get; init; } = string.Empty;

    [Range(1, 10)]
    public int Count { get; init; }
}

/// <summary>A body carrying a timestamp that must include a UTC offset.</summary>
public sealed record TimestampRequest(DateTimeOffset At);
