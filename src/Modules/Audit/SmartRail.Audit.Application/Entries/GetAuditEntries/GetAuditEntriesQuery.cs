using SmartRail.Audit.Contracts;
using SmartRail.SharedKernel.Application.Messaging;
using SmartRail.SharedKernel.Paging;

namespace SmartRail.Audit.Application.Entries.GetAuditEntries;

/// <summary>
/// The Auditor's filtered, paged list, newest first (AUD-01, AUD-02). A blank outcome is no filter;
/// the validator refuses any outcome other than <c>succeeded</c> or <c>denied</c>.
/// </summary>
/// <param name="ActorId">Only entries of this staff member.</param>
/// <param name="From">Only entries at or after this time, inclusive.</param>
/// <param name="To">Only entries at or before this time, inclusive.</param>
/// <param name="Outcome">The wire name of the outcome to show, or <c>null</c> for both.</param>
/// <param name="Page">The one-based page number.</param>
/// <param name="PageSize">How many entries one page holds; at most 100 (design D6).</param>
public sealed record GetAuditEntriesQuery(
    Guid? ActorId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Outcome,
    int Page,
    int PageSize) : IQuery<PagedResult<AuditEntryListItem>>;
