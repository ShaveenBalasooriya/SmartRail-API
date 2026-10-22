using System.ComponentModel.DataAnnotations;
using SmartRail.Host.Http;

namespace SmartRail.Host.IntegrationTests.Infrastructure;

/// <summary>
/// Endpoints that exist only while the integration tests run. They let the shared HTTP contract be
/// asserted without inventing production endpoints: an enumeration and a timestamp to prove the
/// JSON rules, a validating body to prove the 400 shape, and a throwing handler to prove the 500
/// shape. They are registered through <see cref="IEndpointRegistration"/>, so they are mapped by
/// the Host but defined in the tests.
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

    /// <summary>The text the throwing endpoint includes in its exception, which must never escape.</summary>
    public const string FailureSecret = "internal identifier 8f2c0d1e must not leak";

    public void Map(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints
            .MapGroup($"{ApiRoutes.BasePath}{Prefix}")
            .WithTags("Test");

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
    }
}

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
