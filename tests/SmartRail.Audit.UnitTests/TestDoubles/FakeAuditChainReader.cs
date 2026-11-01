using System.Runtime.CompilerServices;
using SmartRail.Audit.Application.Entries;
using SmartRail.Audit.Domain.Entries;

namespace SmartRail.Audit.UnitTests.TestDoubles;

/// <summary>
/// Stands in for the chain reader. Like the real one it streams the entries one at a time in chain
/// order rather than returning the whole record, so the integrity check is exercised the way it runs
/// against the database.
/// </summary>
internal sealed class FakeAuditChainReader(params AuditEntry[] entries) : IAuditChainReader
{
    private readonly AuditEntry[] _entries = entries;

    public async IAsyncEnumerable<AuditEntry> ReadChainAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var entry in _entries)
        {
            await Task.Yield();
            yield return entry;
        }
    }
}
