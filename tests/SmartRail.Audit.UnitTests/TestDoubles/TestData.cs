using SmartRail.Audit.Contracts;
using SmartRail.Audit.Domain.Entries;

namespace SmartRail.Audit.UnitTests.TestDoubles;

/// <summary>
/// Small builders for valid audit data. Every call returns fresh identifiers, so no test can see
/// another test's data. The detail builder makes a plausible stored entry; the chain builders make
/// a genuine hash chain, which the integrity tests then alter the way a direct SQL edit would.
/// </summary>
internal static class TestData
{
    internal static readonly DateTimeOffset Now = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    internal static readonly Guid ActorStaffId = Guid.Parse("0199c3f1-1111-7a2b-8c3d-000000000001");

    /// <summary>One entry as the entry reader returns it, with a unique identifier.</summary>
    internal static AuditEntryDetail Entry(
        Guid? id = null,
        long chainPosition = 1,
        DateTimeOffset? occurredAt = null,
        AuditOutcome outcome = AuditOutcome.Succeeded,
        Guid? actorStaffId = null,
        string action = "publish_delay")
    {
        var actor = actorStaffId ?? (outcome == AuditOutcome.Succeeded ? ActorStaffId : null);
        var succeeded = outcome == AuditOutcome.Succeeded;

        return new AuditEntryDetail(
            id ?? Guid.CreateVersion7(),
            chainPosition,
            occurredAt ?? Now.AddMinutes(chainPosition),
            actor,
            actor is null ? null : "network_controller",
            action,
            outcome.ToString().ToLowerInvariant(),
            succeeded ? "journey" : null,
            succeeded ? Guid.CreateVersion7().ToString() : null,
            Before: null,
            After: null,
            RelatedEntryId: null,
            PreviousHash: new string('0', AuditEntryHash.HexLength),
            EntryHash: new string('a', AuditEntryHash.HexLength));
    }

    /// <summary>A genuine chain of <paramref name="count" /> entries, each linked to the one before.</summary>
    internal static List<AuditEntry> Chain(int count)
    {
        var entries = new List<AuditEntry>(count);
        var previousHash = AuditEntryHash.GenesisPreviousHash;

        for (var position = 1; position <= count; position++)
        {
            var entry = ChainEntry(position, previousHash);
            entries.Add(entry);
            previousHash = entry.EntryHash;
        }

        return entries;
    }

    /// <summary>One entry of a chain, linked to the previous hash it is given.</summary>
    internal static AuditEntry ChainEntry(long position, string previousHash)
    {
        var result = AuditEntry.Create(
            Guid.CreateVersion7(),
            position,
            Now.AddMinutes(position),
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
}
