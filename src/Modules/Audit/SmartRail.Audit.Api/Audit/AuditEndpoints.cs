using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SmartRail.Audit.Application.Entries.CheckAuditIntegrity;
using SmartRail.Audit.Application.Entries.GetAuditEntries;
using SmartRail.Audit.Application.Entries.GetAuditEntry;
using SmartRail.Audit.Contracts;
using SmartRail.SharedKernel.Api;
using SmartRail.SharedKernel.Api.Authorization;
using SmartRail.SharedKernel.Application.Abstractions;
using SmartRail.SharedKernel.Paging;

namespace SmartRail.Audit.Api.Audit;

/// <summary>
/// The Auditor's read-only access to the record (design D6): the filtered, paged list newest first,
/// one entry in full, and the chain integrity check. All are Auditor-only, and there is deliberately
/// no state-changing route, because no role may update or delete an entry (AUD-06).
/// </summary>
public sealed class AuditEndpoints : ICarterModule
{
    /// <inheritdoc />
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/audit")
            .WithTags("Audit")
            .RequireStaffRole(Roles.Auditor);

        group
            .MapGet("/entries", GetEntriesAsync)
            .WithName("GetAuditEntries")
            .WithDescription(
                "Auditor only. Entries are ordered by chain position, highest first. The page size is "
                + "100 or less and defaults to 25, and a range whose start is after its end is refused. "
                + "The list omits the before and after values and the chain hashes; read one entry for those.")
            .Produces<PagedResult<AuditEntryListItem>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group
            .MapGet("/entries/{id:guid}", GetEntryAsync)
            .WithName("GetAuditEntry")
            .WithDescription(
                "Auditor only. The before and after values are JSON values, and a correction carries "
                + "the identifier of the entry for the change it corrects.")
            .Produces<AuditEntryDetail>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group
            .MapGet("/integrity", CheckIntegrityAsync)
            .WithName("CheckAuditIntegrity")
            .WithDescription(
                "Auditor only. Entries are streamed in chain order, and the check recomputes each "
                + "entry's hash and its link to the entry before it, stopping at the first failure. "
                + "An intact record reports how many entries were checked; a broken one names the "
                + "first chain position that failed. An empty record is intact with zero entries checked.")
            .Produces<AuditIntegrityReport>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
    }

    private static async Task<IResult> GetEntriesAsync(
        Guid? actorId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? outcome,
        int? page,
        int? pageSize,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new GetAuditEntriesQuery(
                actorId,
                from,
                to,
                outcome,
                page ?? 1,
                pageSize ?? PageRequest.DefaultPageSize),
            cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblemDetails();
    }

    private static async Task<IResult> GetEntryAsync(
        Guid id,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetAuditEntryQuery(id), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblemDetails();
    }

    private static async Task<IResult> CheckIntegrityAsync(
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new CheckAuditIntegrityQuery(), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblemDetails();
    }
}
