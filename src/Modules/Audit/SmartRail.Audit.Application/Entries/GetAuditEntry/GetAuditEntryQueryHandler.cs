using SmartRail.Audit.Contracts;
using SmartRail.SharedKernel.Application.Messaging;
using SmartRail.SharedKernel.Results;

namespace SmartRail.Audit.Application.Entries.GetAuditEntry;

internal sealed class GetAuditEntryQueryHandler(IAuditEntryReader entries)
    : IQueryHandler<GetAuditEntryQuery, AuditEntryDetail>
{
    private static readonly Error EntryNotFound =
        Error.NotFound("audit_entry.not_found", "No audit entry matches this identifier.");

    public async Task<Result<AuditEntryDetail>> Handle(
        GetAuditEntryQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var entry = await entries.GetEntryAsync(request.EntryId, cancellationToken);

        return entry is null
            ? Result.Failure<AuditEntryDetail>(EntryNotFound)
            : Result.Success(entry);
    }
}
