using SmartRail.Audit.Contracts;
using SmartRail.SharedKernel.Paging;

namespace SmartRail.Audit.Application.Entries;

/// <summary>
/// Reads the audit record from the module's own schema. It is the read side of the Auditor's
/// endpoints and never exposes a domain entity (design D6).
/// </summary>
public interface IAuditEntryReader
{
    /// <summary>One page of the entries matching <paramref name="filter"/>, newest first.</summary>
    Task<PagedResult<AuditEntryListItem>> GetEntriesAsync(
        AuditEntryFilter filter,
        PageRequest page,
        CancellationToken cancellationToken = default);

    /// <summary>One entry in full, or <see langword="null"/> when the ID is unknown.</summary>
    Task<AuditEntryDetail?> GetEntryAsync(Guid entryId, CancellationToken cancellationToken = default);
}
