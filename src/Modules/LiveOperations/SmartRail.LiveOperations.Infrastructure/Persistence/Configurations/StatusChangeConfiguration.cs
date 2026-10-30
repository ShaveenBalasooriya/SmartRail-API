using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartRail.LiveOperations.Domain.Changes;
using SmartRail.LiveOperations.Domain.LiveState;

namespace SmartRail.LiveOperations.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="StatusChange"/> to <c>operations.status_changes</c>.</summary>
internal sealed class StatusChangeConfiguration : IEntityTypeConfiguration<StatusChange>
{
    public void Configure(EntityTypeBuilder<StatusChange> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "status_changes",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_status_changes_change_type",
                    "change_type IN ('delay','cancellation','termination','platform_change','correction')");
                table.HasCheckConstraint(
                    "CK_status_changes_provenance",
                    "provenance IN ('automatically_published')");
            });

        builder.HasKey(change => change.Id);

        builder.Property(change => change.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(change => change.JourneyId).HasColumnName("journey_id");
        builder.Property(change => change.Type).HasColumnName("change_type").HasConversion(
                type => ToStorage(type),
                value => FromStorage(value)).IsRequired();
        builder.Property(change => change.StopSequence).HasColumnName("stop_sequence");
        builder.Property(change => change.DelayMinutes).HasColumnName("delay_minutes");
        builder.Property(change => change.NewPlatformId).HasColumnName("new_platform_id");
        builder.Property(change => change.PreviousPlatformId).HasColumnName("previous_platform_id");
        builder.Property(change => change.CorrectsChangeId).HasColumnName("corrects_change_id");
        builder.Property(change => change.Provenance).HasColumnName("provenance").HasConversion(
                provenance => ToStorage(provenance),
                value => FromStorageProvenance(value)).IsRequired();
        builder.Property(change => change.PublishedBy).HasColumnName("published_by");
        builder.Property(change => change.PublishedAt).HasColumnName("published_at");

        // The audit entry the change produced: a plain identifier, not a foreign key, because audit
        // entries live in another module's schema (package decision D-8).
        builder.Property(change => change.AuditEntryId).HasColumnName("audit_entry_id");
        builder
            .Property(change => change.FactOrder)
            .HasColumnName("fact_order")
            .HasDefaultValueSql(
                $"nextval('{LiveOperationsDbContext.Schema}.{LiveOperationsDbContext.FactOrderSequence}')")
            .ValueGeneratedOnAdd();

        // The facts of one journey are read in the order they were committed.
        builder.HasIndex(change => new { change.JourneyId, change.FactOrder });

        // A change can be corrected once; the unique index is also what makes a second correction
        // of the same original fail (change-correction spec).
        builder.HasIndex(change => change.CorrectsChangeId).IsUnique();

        builder
            .HasOne<JourneyLiveState>()
            .WithMany()
            .HasForeignKey(change => change.JourneyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne<StatusChange>()
            .WithMany()
            .HasForeignKey(change => change.CorrectsChangeId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static string ToStorage(StatusChangeType type) => type switch
    {
        StatusChangeType.Delay => "delay",
        StatusChangeType.Cancellation => "cancellation",
        StatusChangeType.Termination => "termination",
        StatusChangeType.PlatformChange => "platform_change",
        StatusChangeType.Correction => "correction",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown status change type."),
    };

    private static StatusChangeType FromStorage(string value) => value switch
    {
        "delay" => StatusChangeType.Delay,
        "cancellation" => StatusChangeType.Cancellation,
        "termination" => StatusChangeType.Termination,
        "platform_change" => StatusChangeType.PlatformChange,
        "correction" => StatusChangeType.Correction,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown status change type."),
    };

    private static string ToStorage(ChangeProvenance provenance) => provenance switch
    {
        ChangeProvenance.AutomaticallyPublished => "automatically_published",
        _ => throw new ArgumentOutOfRangeException(nameof(provenance), provenance, "Unknown change provenance."),
    };

    private static ChangeProvenance FromStorageProvenance(string value) => value switch
    {
        "automatically_published" => ChangeProvenance.AutomaticallyPublished,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown change provenance."),
    };
}
