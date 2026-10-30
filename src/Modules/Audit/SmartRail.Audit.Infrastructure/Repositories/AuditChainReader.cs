using Microsoft.EntityFrameworkCore;
using SmartRail.Audit.Application.Entries;
using SmartRail.Audit.Domain.Entries;
using SmartRail.Audit.Infrastructure.Persistence;

namespace SmartRail.Audit.Infrastructure.Repositories;

/// <summary>
/// Streams the whole record in chain order for the integrity check. The entities are untracked and
/// never materialised as one list, so the check can walk a long record without holding it in memory
/// (design D6, AUD-05).
/// </summary>
internal sealed class AuditChainReader(AuditDbContext database) : IAuditChainReader
{
    /// <inheritdoc />
    public IAsyncEnumerable<AuditEntry> ReadChainAsync(CancellationToken cancellationToken = default) =>
        database.Entries
            .AsNoTracking()
            .OrderBy(entry => entry.ChainPosition)
            .AsAsyncEnumerable();
}
