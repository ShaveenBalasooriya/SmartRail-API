namespace SmartRail.Audit.Infrastructure.Persistence;

/// <summary>
/// What one call to <see cref="AuditChainWriter"/> did: an entry was appended, or the denial it
/// describes was already recorded and nothing was written (design D5).
/// </summary>
internal readonly record struct AuditAppendResult(bool Written, Guid EntryId)
{
    /// <summary>The only entry in the chain that carries <paramref name="entryId"/>.</summary>
    public static AuditAppendResult Appended(Guid entryId) => new(Written: true, entryId);

    /// <summary>The source event already has an entry, so a redelivery adds nothing.</summary>
    public static AuditAppendResult Duplicate() => new(Written: false, EntryId: Guid.Empty);
}
