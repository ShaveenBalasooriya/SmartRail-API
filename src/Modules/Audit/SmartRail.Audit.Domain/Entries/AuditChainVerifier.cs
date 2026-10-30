namespace SmartRail.Audit.Domain.Entries;

/// <summary>
/// Recomputes the single global hash chain (design D3, AUD-05) by walking the entries in chain
/// position order and stopping at the first entry that fails. An entry fails when its position is not
/// exactly the previous one plus one, when its previous hash is not the hash of the entry before it,
/// or when its stored hash is not the hash of its stored content and link. Because the entries are
/// streamed, the record is never held in memory as a whole.
/// </summary>
public static class AuditChainVerifier
{
    /// <summary>
    /// Verifies the stream of <paramref name="entries"/>, which must already be ordered by chain
    /// position. An empty stream is intact with zero entries checked.
    /// </summary>
    /// <remarks>
    /// A gap is reported at the position the record should have held next: a record whose entries
    /// are 1, 2 and 4 fails at 3, because position 3 is the first one that cannot be verified.
    /// </remarks>
    public static async Task<AuditChainVerification> VerifyAsync(
        IAsyncEnumerable<AuditEntry> entries,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var expectedPosition = 1L;
        var previousHash = AuditEntryHash.GenesisPreviousHash;
        var entriesChecked = 0L;

        await foreach (var entry in entries.WithCancellation(cancellationToken))
        {
            ArgumentNullException.ThrowIfNull(entry);

            entriesChecked++;

            if (entry.ChainPosition != expectedPosition)
            {
                return AuditChainVerification.Broken(entriesChecked, expectedPosition);
            }

            if (!string.Equals(entry.PreviousHash, previousHash, StringComparison.Ordinal))
            {
                return AuditChainVerification.Broken(entriesChecked, entry.ChainPosition);
            }

            if (!string.Equals(entry.EntryHash, entry.RecomputeHash(), StringComparison.Ordinal))
            {
                return AuditChainVerification.Broken(entriesChecked, entry.ChainPosition);
            }

            previousHash = entry.EntryHash;
            expectedPosition++;
        }

        return AuditChainVerification.Intact(entriesChecked);
    }
}
