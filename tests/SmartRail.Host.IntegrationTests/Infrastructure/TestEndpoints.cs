using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using MediatR;
using SmartRail.Audit.Contracts;
using SmartRail.Host.Http;
using SmartRail.IdentityAccess.Domain.Entities;
using SmartRail.IdentityAccess.Infrastructure.Persistence;
using SmartRail.LiveOperations.Application.LiveState.ApplyJourneyFact;
using SmartRail.LiveOperations.Domain.Calculation;
using SmartRail.RailNetwork.Domain.Entities;
using SmartRail.RailNetwork.Domain.ValueObjects;
using SmartRail.RailNetwork.Infrastructure.Persistence;
using SmartRail.SharedKernel.Api;
using SmartRail.SharedKernel.Api.Authorization;
using SmartRail.SharedKernel.Application.Abstractions;
using SmartRail.SharedKernel.Infrastructure.Persistence;
using JourneyEventType = SmartRail.LiveOperations.Contracts.JourneyEventType;

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

    /// <summary>
    /// A POST that writes through two module contexts and can then fail or answer 500, to prove the
    /// request transaction spans both and rolls back.
    /// </summary>
    public const string TransactionWriteRoute = $"{ApiRoutes.BasePath}{Prefix}/transaction-write";

    /// <summary>A GET that reports whether the request transaction is active.</summary>
    public const string TransactionReadRoute = $"{ApiRoutes.BasePath}{Prefix}/transaction-read";

    /// <summary>
    /// A POST that records an audit entry, optionally writing a network line first, and can then
    /// fail or answer 500, to prove the entry commits and rolls back with the caller's change.
    /// </summary>
    public const string AuditRecordRoute = $"{ApiRoutes.BasePath}{Prefix}/audit-record";

    /// <summary>
    /// A POST that applies a journey fact through MediatR and records an audit entry in the same
    /// request, and can then fail or answer 500, to prove the fact, its outbox row and the entry are
    /// one unit.
    /// </summary>
    public const string JourneyFactRoute = $"{ApiRoutes.BasePath}{Prefix}/journey-fact";

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

        group.MapPost("/transaction-write", async (
                Guid staffId,
                Guid lineId,
                string? mode,
                IdentityDbContext identity,
                RailNetworkDbContext network) =>
            {
                // Both contexts save in the same request. The Host's request transaction is what
                // makes the two writes one unit, so this route proves it rolls them back together.
                var staff = StaffMember.Create(
                    staffId,
                    $"tx-subject-{staffId:N}",
                    $"tx-user-{staffId:N}",
                    "Transaction Test",
                    DateTimeOffset.UtcNow).Value;

                identity.StaffMembers.Add(staff);
                await identity.SaveChangesAsync();

                var line = Line.Create(
                    lineId,
                    LineCode.Create($"TX-{lineId:N}").Value,
                    LineColor.Create("#64748b").Value,
                    "Transaction Test Line").Value;

                network.Lines.Add(line);
                await network.SaveChangesAsync();

                if (string.Equals(mode, "throw", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "The transaction test endpoint failed after writing through both contexts.");
                }

                if (string.Equals(mode, "500", StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status500InternalServerError,
                        title: "Transaction test failure");
                }

                return Results.Ok(new TransactionWriteResponse(staffId, lineId));
            })
            .WithName("TestTransactionWrite")
            .WithSummary("Writes through two module contexts, then succeeds, throws or answers 500.")
            .WithDescription("Test-only. Access: none.")
            .Produces<TransactionWriteResponse>()
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapGet("/transaction-read", (IRequestTransaction requestTransaction) =>
                Results.Ok(new TransactionReadResponse(requestTransaction.IsActive)))
            .WithName("TestTransactionRead")
            .WithSummary("Reports whether this request has an active request transaction.")
            .WithDescription("Test-only. Access: none.")
            .Produces<TransactionReadResponse>();

        group.MapPost("/audit-record", async (
                Guid staffId,
                string? action,
                Guid? lineId,
                string? mode,
                IAuditRecorder auditRecorder,
                RailNetworkDbContext network) =>
            {
                // A second module's write, so a failed request shows that the audit entry and the
                // caller's change are one unit.
                if (lineId is { } id)
                {
                    var line = Line.Create(
                        id,
                        LineCode.Create($"AU-{id:N}").Value,
                        LineColor.Create("#64748b").Value,
                        "Audit Test Line").Value;

                    network.Lines.Add(line);
                    await network.SaveChangesAsync();
                }

                var entryId = await auditRecorder.RecordAsync(new AuditRecordRequest(
                    staffId,
                    "network_controller",
                    action ?? "publish_delay",
                    "journey",
                    Guid.CreateVersion7().ToString(),
                    """{"status":"scheduled"}""",
                    """{"status":"delayed","minutes":5}""",
                    null));

                if (string.Equals(mode, "throw", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "The audit test endpoint failed after recording an entry.");
                }

                if (string.Equals(mode, "500", StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status500InternalServerError,
                        title: "Audit test failure");
                }

                return Results.Ok(new AuditRecordResponse(entryId));
            })
            .WithName("TestAuditRecord")
            .WithSummary("Records an audit entry, then succeeds, throws or answers 500.")
            .WithDescription("Test-only. Access: none.")
            .Produces<AuditRecordResponse>()
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapPost("/journey-fact", async (
                Guid journeyId,
                int stopSequence,
                DateTimeOffset departedAt,
                Guid staffId,
                string? mode,
                ISender sender,
                IAuditRecorder auditRecorder) =>
            {
                // The fact handler locks the journey's live-state row and writes the journey event
                // and its outbox row; the audit entry is recorded in the same request, so this route
                // proves the three commit or roll back together.
                var fact = new DepartureFact(stopSequence, departedAt, DateTimeOffset.UtcNow, stopSequence);

                var result = await sender.Send(new ApplyJourneyFactCommand(
                    journeyId,
                    fact,
                    JourneyEventType.DepartureLogged,
                    Guid.CreateVersion7(),
                    fact.DepartedAt,
                    fact.RecordedAt));

                if (!result.IsSuccess)
                {
                    return result.ToProblemDetails();
                }

                var entryId = await auditRecorder.RecordAsync(new AuditRecordRequest(
                    staffId,
                    "guard",
                    "departure_logged",
                    "journey",
                    journeyId.ToString(),
                    """{"status":"scheduled"}""",
                    """{"status":"running"}""",
                    null));

                if (string.Equals(mode, "throw", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "The journey-fact test endpoint failed after applying the fact.");
                }

                if (string.Equals(mode, "500", StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status500InternalServerError,
                        title: "Journey fact test failure");
                }

                return Results.Ok(new JourneyFactResponse(entryId));
            })
            .WithName("TestJourneyFact")
            .WithSummary("Applies a journey fact and records an audit entry, then succeeds, throws or answers 500.")
            .WithDescription("Test-only. Access: none.")
            .Produces<JourneyFactResponse>()
            .ProducesProblem(StatusCodes.Status500InternalServerError);
    }
}

/// <summary>The provider the API key identified, as the provider test endpoint returns it.</summary>
public sealed record ProviderIdentityResponse(string ProviderId);

/// <summary>The identifiers the transaction-write test endpoint wrote.</summary>
public sealed record TransactionWriteResponse(Guid StaffId, Guid LineId);

/// <summary>Whether the request that answered this response had an active request transaction.</summary>
public sealed record TransactionReadResponse(bool IsActive);

/// <summary>The audit entry the audit test endpoint recorded.</summary>
public sealed record AuditRecordResponse(Guid EntryId);

/// <summary>The audit entry the journey-fact test endpoint recorded alongside the applied fact.</summary>
public sealed record JourneyFactResponse(Guid EntryId);

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
