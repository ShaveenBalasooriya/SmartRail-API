using System.Reflection;
using SmartRail.Audit.Domain.Entries;

namespace SmartRail.Audit.UnitTests.Entries;

/// <summary>
/// The chain verification loop of design D3 and AUD-05, proved without a database: a genuine chain
/// verifies, an empty record verifies with nothing checked, and a row altered the way a direct SQL
/// edit alters it (a changed value, a changed link, or a removed position) is caught at exactly the
/// entry that no longer fits.
/// </summary>
public sealed class AuditChainVerifierTests
{
    private static readonly Guid ActorStaffId = Guid.Parse("0199c3f1-1111-7a2b-8c3d-000000000001");

    private static readonly DateTimeOffset OccurredAt = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task An_untouched_chain_is_intact_with_the_entry_count()
    {
        var verification = await AuditChainVerifier.VerifyAsync(Stream(Chain(3)));

        Assert.True(verification.IsIntact);
        Assert.Equal(3, verification.EntriesChecked);
        Assert.Null(verification.FirstFailingPosition);
    }

    [Fact]
    public async Task An_empty_record_is_intact_with_zero_entries()
    {
        var verification = await AuditChainVerifier.VerifyAsync(Stream([]));

        Assert.True(verification.IsIntact);
        Assert.Equal(0, verification.EntriesChecked);
        Assert.Null(verification.FirstFailingPosition);
    }

    [Fact]
    public async Task An_edited_after_value_breaks_the_chain_at_that_entry()
    {
        var entries = Chain(3);

        // The stored hash still covers the original after-value, exactly as it would after a direct
        // SQL edit that disables the append-only trigger, edits the row and re-enables it.
        SetStored(entries[1], nameof(AuditEntry.After), """{"status":"cancelled"}""");

        var verification = await AuditChainVerifier.VerifyAsync(Stream(entries));

        Assert.False(verification.IsIntact);
        Assert.Equal(2, verification.FirstFailingPosition);
        Assert.Equal(2, verification.EntriesChecked);
    }

    [Fact]
    public async Task An_edited_previous_hash_breaks_the_chain_at_that_entry()
    {
        var entries = Chain(3);

        // The link no longer points at the hash of the entry before it, so the entry stops verifying
        // even though its own content is untouched.
        SetStored(entries[1], nameof(AuditEntry.PreviousHash), new string('a', AuditEntryHash.HexLength));

        var verification = await AuditChainVerifier.VerifyAsync(Stream(entries));

        Assert.False(verification.IsIntact);
        Assert.Equal(2, verification.FirstFailingPosition);
        Assert.Equal(2, verification.EntriesChecked);
    }

    [Fact]
    public async Task A_missing_position_breaks_the_chain_at_the_gap()
    {
        var entries = Chain(2);
        entries.Add(Entry(position: 4, previousHash: entries[1].EntryHash));

        var verification = await AuditChainVerifier.VerifyAsync(Stream(entries));

        Assert.False(verification.IsIntact);

        // Position 3 is the first one that cannot be verified, and the check stops once it sees
        // position 4 where 3 was due.
        Assert.Equal(3, verification.FirstFailingPosition);
        Assert.Equal(3, verification.EntriesChecked);
    }

    /// <summary>A genuine chain of <paramref name="count"/> entries, each linked to the one before.</summary>
    private static List<AuditEntry> Chain(int count)
    {
        var entries = new List<AuditEntry>(count);
        var previousHash = AuditEntryHash.GenesisPreviousHash;

        for (var position = 1; position <= count; position++)
        {
            var entry = Entry(position, previousHash);
            entries.Add(entry);
            previousHash = entry.EntryHash;
        }

        return entries;
    }

    private static AuditEntry Entry(long position, string previousHash)
    {
        var result = AuditEntry.Create(
            Guid.CreateVersion7(),
            position,
            OccurredAt.AddMinutes(position),
            ActorStaffId,
            "network_controller",
            "publish_delay",
            AuditOutcome.Succeeded,
            "journey",
            Guid.CreateVersion7().ToString(),
            before: """{"status":"scheduled"}""",
            after: """{"status":"delayed"}""",
            relatedEntryId: null,
            sourceEventId: null,
            previousHash);

        Assert.True(result.IsSuccess, $"The entry should be valid: {result.Error.Description}");

        return result.Value;
    }

    /// <summary>
    /// Rewrites a stored value the way a direct SQL edit would, leaving the entry's hash as it was.
    /// The setters are private because an entry is append-only in the application, so the test reaches
    /// them the one way that does not add a tampering method to the domain.
    /// </summary>
    private static void SetStored(AuditEntry entry, string propertyName, string value)
    {
        var setter = typeof(AuditEntry)
            .GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?
            .GetSetMethod(nonPublic: true);

        Assert.NotNull(setter);
        setter.Invoke(entry, [value]);
    }

    /// <summary>Streams the entries the way the reader port does, one at a time.</summary>
    private static async IAsyncEnumerable<AuditEntry> Stream(IEnumerable<AuditEntry> entries)
    {
        foreach (var entry in entries)
        {
            await Task.Yield();
            yield return entry;
        }
    }
}
