using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartRail.Audit.Domain.Entries;

namespace SmartRail.Audit.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="AuditEntry"/> to <c>audit.entries</c>.</summary>
internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("entries");
        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(entry => entry.ChainPosition).HasColumnName("chain_position");
        builder.Property(entry => entry.OccurredAt).HasColumnName("occurred_at");
        builder.Property(entry => entry.ActorStaffId).HasColumnName("actor_staff_id");
        builder.Property(entry => entry.ActorRole).HasColumnName("actor_role");
        builder.Property(entry => entry.Action).HasColumnName("action").IsRequired();
        builder.Property(entry => entry.Outcome).HasColumnName("outcome").HasConversion(
                outcome => ToStorage(outcome),
                value => FromStorage(value)).IsRequired();
        builder.Property(entry => entry.TargetType).HasColumnName("target_type");
        builder.Property(entry => entry.TargetId).HasColumnName("target_id");
        builder.Property(entry => entry.Before).HasColumnName("before").HasColumnType("jsonb");
        builder.Property(entry => entry.After).HasColumnName("after").HasColumnType("jsonb");
        builder.Property(entry => entry.RelatedEntryId).HasColumnName("related_entry_id");
        builder.Property(entry => entry.SourceEventId).HasColumnName("source_event_id");
        builder.Property(entry => entry.PreviousHash).HasColumnName("previous_hash").HasMaxLength(64).IsRequired();
        builder.Property(entry => entry.EntryHash).HasColumnName("entry_hash").HasMaxLength(64).IsRequired();

        // Positions are assigned once and never reused, so the chain has no duplicates. A denial's
        // source event may be recorded only once; PostgreSQL permits many nulls in a unique index,
        // which is what successful changes have (design D2, D5).
        builder.HasIndex(entry => entry.ChainPosition).IsUnique();
        builder.HasIndex(entry => entry.SourceEventId).IsUnique();

        // A correction points at the entry it corrects; neither cascade nor set-null is acceptable on
        // an append-only record, so a referenced entry cannot be deleted at all.
        builder
            .HasOne<AuditEntry>()
            .WithMany()
            .HasForeignKey(entry => entry.RelatedEntryId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static string ToStorage(AuditOutcome outcome) => outcome switch
    {
        AuditOutcome.Succeeded => "succeeded",
        AuditOutcome.Denied => "denied",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown audit outcome."),
    };

    private static AuditOutcome FromStorage(string value) => value switch
    {
        "succeeded" => AuditOutcome.Succeeded,
        "denied" => AuditOutcome.Denied,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown audit outcome."),
    };
}
