using SmartRail.Audit.Domain.Entries;

namespace SmartRail.Audit.Application.Entries;

/// <summary>
/// Streams the whole record in chain order for the integrity check (design D6, AUD-05). It is kept
/// apart from <see cref="IAuditEntryReader"/> because the check needs the entries themselves: the
/// hash is recomputed from the stored content by the domain, not by the read side, so the record is
/// never loaded into memory as a whole.
/// </summary>
public interface IAuditChainReader
{
    /// <summary>Every entry, one at a time, in ascending chain position.</summary>
    IAsyncEnumerable<AuditEntry> ReadChainAsync(CancellationToken cancellationToken = default);
}
