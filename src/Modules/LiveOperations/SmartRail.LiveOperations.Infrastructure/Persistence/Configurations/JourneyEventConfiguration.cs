using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartRail.LiveOperations.Domain.LiveState;

namespace SmartRail.LiveOperations.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="JourneyEvent"/> to <c>operations.journey_events</c>.</summary>
internal sealed class JourneyEventConfiguration : IEntityTypeConfiguration<JourneyEvent>
{
    public void Configure(EntityTypeBuilder<JourneyEvent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "journey_events",
            table => table.HasCheckConstraint(
                "CK_journey_events_event_type",
                "event_type IN ('departure_logged','delay_published','cancellation_published','platform_changed','correction_published','journey_completed')"));

        builder.HasKey(journeyEvent => journeyEvent.Id);

        builder.Property(journeyEvent => journeyEvent.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(journeyEvent => journeyEvent.JourneyId).HasColumnName("journey_id");
        builder.Property(journeyEvent => journeyEvent.SequenceNumber).HasColumnName("sequence_number");
        builder
            .Property(journeyEvent => journeyEvent.StreamPosition)
            .HasColumnName("stream_position")
            .HasDefaultValueSql(
                $"nextval('{LiveOperationsDbContext.Schema}.{LiveOperationsDbContext.StreamPositionSequence}')")
            .ValueGeneratedOnAdd();
        builder.Property(journeyEvent => journeyEvent.EventType).HasColumnName("event_type").HasConversion(
                eventType => ToStorage(eventType),
                value => FromStorage(value)).IsRequired();
        builder.Property(journeyEvent => journeyEvent.StatusChangeId).HasColumnName("status_change_id");
        builder.Property(journeyEvent => journeyEvent.DepartureLogId).HasColumnName("departure_log_id");
        builder.Property(journeyEvent => journeyEvent.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        builder.Property(journeyEvent => journeyEvent.OccurredAt).HasColumnName("occurred_at");
        builder.Property(journeyEvent => journeyEvent.RecordedAt).HasColumnName("recorded_at");

        // Per-journey numbers are unique, and the stream is read after a position.
        builder.HasIndex(journeyEvent => new { journeyEvent.JourneyId, journeyEvent.SequenceNumber }).IsUnique();
        builder.HasIndex(journeyEvent => journeyEvent.StreamPosition);

        builder
            .HasOne<JourneyLiveState>()
            .WithMany()
            .HasForeignKey(journeyEvent => journeyEvent.JourneyId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static string ToStorage(JourneyEventType eventType) => eventType switch
    {
        JourneyEventType.DepartureLogged => "departure_logged",
        JourneyEventType.DelayPublished => "delay_published",
        JourneyEventType.CancellationPublished => "cancellation_published",
        JourneyEventType.PlatformChanged => "platform_changed",
        JourneyEventType.CorrectionPublished => "correction_published",
        JourneyEventType.JourneyCompleted => "journey_completed",
        _ => throw new ArgumentOutOfRangeException(nameof(eventType), eventType, "Unknown journey event type."),
    };

    private static JourneyEventType FromStorage(string value) => value switch
    {
        "departure_logged" => JourneyEventType.DepartureLogged,
        "delay_published" => JourneyEventType.DelayPublished,
        "cancellation_published" => JourneyEventType.CancellationPublished,
        "platform_changed" => JourneyEventType.PlatformChanged,
        "correction_published" => JourneyEventType.CorrectionPublished,
        "journey_completed" => JourneyEventType.JourneyCompleted,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown journey event type."),
    };
}
