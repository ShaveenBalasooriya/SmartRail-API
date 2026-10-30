using SmartRail.Audit.Domain.Entries;

namespace SmartRail.Audit.UnitTests.Entries;

/// <summary>
/// The hash contract of design D3: the same content hashes the same, every covered field and the
/// link to the previous entry change the hash, and values the design excludes (source event ID,
/// JSON key order, sub-microsecond ticks) do not.
/// </summary>
public sealed class AuditEntryHashTests
{
    private static readonly Guid ActorStaffId = Guid.Parse("0199c3f1-1111-7a2b-8c3d-000000000001");
    private static readonly Guid RelatedEntryId = Guid.Parse("0199c3f1-1111-7a2b-8c3d-000000000002");
    private static readonly Guid SourceEventId = Guid.Parse("0199c3f1-1111-7a2b-8c3d-000000000003");

    /// <summary>09:15:30.123450 on 2026-10-23, with four sub-microsecond ticks added.</summary>
    private static readonly DateTimeOffset OccurredAt =
        new DateTimeOffset(2026, 10, 23, 9, 15, 30, TimeSpan.Zero).AddTicks(1_234_504);

    private static readonly Content Baseline = new(
        ChainPosition: 1,
        OccurredAt: OccurredAt,
        ActorStaffId: ActorStaffId,
        ActorRole: "guard",
        Action: "departure.logged",
        Outcome: AuditOutcome.Succeeded,
        TargetType: "journey",
        TargetId: "0199c3f1-1111-7a2b-8c3d-000000000004",
        Before: "{\"expectedDeparture\":\"08:40:00\"}",
        After: "{\"actualDeparture\":\"08:42:00\"}",
        RelatedEntryId: null,
        SourceEventId: null,
        PreviousHash: AuditEntryHash.GenesisPreviousHash);

    [Fact]
    public void Identical_content_gives_an_identical_hash()
    {
        var first = Entry(Baseline);
        var second = Entry(Baseline);

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(first.EntryHash, second.EntryHash);
        Assert.Equal(AuditEntryHash.HexLength, first.EntryHash.Length);
        Assert.Equal(first.EntryHash, first.RecomputeHash());
    }

    /// <summary>
    /// Pins the exact canonical form and hash input, not just their stability: the expected value was
    /// computed independently with SHA-256 over the canonical JSON plus the 64-zero genesis hash.
    /// </summary>
    [Fact]
    public void A_known_entry_hashes_to_the_expected_value()
    {
        Assert.Equal(
            "ebf7df32cbdb6a4141fa93021957f887aac48abd6c479695601ef501af1d61e4",
            Entry(Baseline).EntryHash);
    }

    [Theory]
    [InlineData("chain_position")]
    [InlineData("occurred_at")]
    [InlineData("actor_staff_id")]
    [InlineData("actor_role")]
    [InlineData("action")]
    [InlineData("outcome")]
    [InlineData("target_type")]
    [InlineData("target_id")]
    [InlineData("before")]
    [InlineData("after")]
    [InlineData("related_entry_id")]
    [InlineData("previous_hash")]
    public void Changing_a_hashed_field_changes_the_hash(string field)
    {
        var baseline = Entry(Baseline);
        var changed = Entry(field switch
        {
            "chain_position" => Baseline with { ChainPosition = 2 },
            "occurred_at" => Baseline with { OccurredAt = OccurredAt.AddSeconds(1) },
            "actor_staff_id" => Baseline with { ActorStaffId = Guid.CreateVersion7() },
            "actor_role" => Baseline with { ActorRole = "network_controller" },
            "action" => Baseline with { Action = "delay.published" },
            "outcome" => Baseline with { Outcome = AuditOutcome.Denied },
            "target_type" => Baseline with { TargetType = "station" },
            "target_id" => Baseline with { TargetId = "station-2" },
            "before" => Baseline with { Before = "{\"expectedDeparture\":\"08:41:00\"}" },
            "after" => Baseline with { After = "{\"actualDeparture\":\"08:43:00\"}" },
            "related_entry_id" => Baseline with { RelatedEntryId = RelatedEntryId },
            "previous_hash" => Baseline with { PreviousHash = new string('1', AuditEntryHash.HexLength) },
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Unknown hashed field."),
        });

        Assert.NotEqual(baseline.EntryHash, changed.EntryHash);
    }

    [Fact]
    public void Genesis_previous_hash_is_sixty_four_zeros()
    {
        Assert.Equal(new string('0', 64), AuditEntryHash.GenesisPreviousHash);
        Assert.Equal(AuditEntryHash.GenesisPreviousHash, Entry(Baseline).PreviousHash);
    }

    [Fact]
    public void Sub_microsecond_ticks_hash_the_same_as_the_microsecond_truncated_value()
    {
        var withSubMicrosecondTicks = Entry(Baseline);
        var truncated = Entry(Baseline with { OccurredAt = OccurredAt.AddTicks(-4) });

        Assert.Equal(truncated.OccurredAt, withSubMicrosecondTicks.OccurredAt);
        Assert.Equal(truncated.EntryHash, withSubMicrosecondTicks.EntryHash);
        Assert.Equal(
            AuditEntryCanonicalizer.Canonicalize(truncated),
            AuditEntryCanonicalizer.Canonicalize(withSubMicrosecondTicks));
        Assert.Contains(
            "\"occurred_at\":\"2026-10-23T09:15:30.123450Z\"",
            AuditEntryCanonicalizer.Canonicalize(withSubMicrosecondTicks),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Source_event_id_changes_nothing()
    {
        var withoutEvent = Entry(Baseline);
        var withEvent = Entry(Baseline with { SourceEventId = SourceEventId });

        Assert.Null(withoutEvent.SourceEventId);
        Assert.Equal(SourceEventId, withEvent.SourceEventId);
        Assert.Equal(withoutEvent.EntryHash, withEvent.EntryHash);
    }

    [Fact]
    public void Before_and_after_key_order_does_not_change_the_hash()
    {
        var first = Entry(Baseline with
        {
            Before = "{\"expectedDeparture\":\"08:40:00\",\"platform\":2,\"nested\":{\"b\":1,\"a\":2}}",
            After = "{\"cause\":\"signal failure\",\"actualDeparture\":\"08:42:00\"}",
        });
        var reordered = Entry(Baseline with
        {
            Before = "{ \"nested\" : { \"a\" : 2, \"b\" : 1 }, \"platform\" : 2, \"expectedDeparture\" : \"08:40:00\" }",
            After = "{\"actualDeparture\":\"08:42:00\",\"cause\":\"signal failure\"}",
        });

        Assert.Equal(first.EntryHash, reordered.EntryHash);
        Assert.Equal(
            AuditEntryCanonicalizer.Canonicalize(first),
            AuditEntryCanonicalizer.Canonicalize(reordered));
    }

    private static AuditEntry Entry(Content content)
    {
        var result = AuditEntry.Create(
            Guid.CreateVersion7(),
            content.ChainPosition,
            content.OccurredAt,
            content.ActorStaffId,
            content.ActorRole,
            content.Action,
            content.Outcome,
            content.TargetType,
            content.TargetId,
            content.Before,
            content.After,
            content.RelatedEntryId,
            content.SourceEventId,
            content.PreviousHash);

        Assert.True(result.IsSuccess, $"The entry should be valid: {result.Error.Description}");

        return result.Value;
    }

    private sealed record Content(
        long ChainPosition,
        DateTimeOffset OccurredAt,
        Guid? ActorStaffId,
        string? ActorRole,
        string Action,
        AuditOutcome Outcome,
        string? TargetType,
        string? TargetId,
        string? Before,
        string? After,
        Guid? RelatedEntryId,
        Guid? SourceEventId,
        string PreviousHash);
}
