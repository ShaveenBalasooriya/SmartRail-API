using SmartRail.Audit.Contracts;
using SmartRail.Audit.Domain.Entries;
using SmartRail.SharedKernel.Application.Messaging;
using SmartRail.SharedKernel.Paging;
using SmartRail.SharedKernel.Results;

namespace SmartRail.Audit.Application.Entries.GetAuditEntries;

internal sealed class GetAuditEntriesQueryHandler(IAuditEntryReader entries)
    : IQueryHandler<GetAuditEntriesQuery, PagedResult<AuditEntryListItem>>
{
    public async Task<Result<PagedResult<AuditEntryListItem>>> Handle(
        GetAuditEntriesQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var filter = new AuditEntryFilter(
            request.ActorId,
            request.From,
            request.To,
            OutcomeOf(request.Outcome));

        var page = new PageRequest(request.Page, request.PageSize);

        return Result.Success(await entries.GetEntriesAsync(filter, page, cancellationToken));
    }

    /// <summary>
    /// Maps the wire value to the stored outcome; a blank value is no filter. The validator has
    /// already refused every other value, and parsing by name keeps a numeric string from being read
    /// as an enum value.
    /// </summary>
    private static AuditOutcome? OutcomeOf(string? outcome)
    {
        if (string.Equals(outcome, "denied", StringComparison.OrdinalIgnoreCase))
        {
            return AuditOutcome.Denied;
        }

        return string.Equals(outcome, "succeeded", StringComparison.OrdinalIgnoreCase)
            ? AuditOutcome.Succeeded
            : null;
    }
}
