namespace SmartRail.Audit.Domain.Entries;

/// <summary>
/// The answer to the integrity check (AUD-05): either the whole record verified, with the number of
/// entries examined, or the first position that failed, with the number examined up to and including
/// that entry.
/// </summary>
/// <param name="IsIntact">Whether every entry examined verified.</param>
/// <param name="EntriesChecked">How many entries were examined, including a failing one.</param>
/// <param name="FirstFailingPosition">
/// The chain position that failed, or <c>null</c> when the record is intact.
/// </param>
public sealed record AuditChainVerification(
    bool IsIntact,
    long EntriesChecked,
    long? FirstFailingPosition)
{
    /// <summary>The record verified end to end.</summary>
    public static AuditChainVerification Intact(long entriesChecked) =>
        new(true, entriesChecked, null);

    /// <summary>The record stopped verifying at <paramref name="firstFailingPosition"/>.</summary>
    public static AuditChainVerification Broken(long entriesChecked, long firstFailingPosition) =>
        new(false, entriesChecked, firstFailingPosition);
}
