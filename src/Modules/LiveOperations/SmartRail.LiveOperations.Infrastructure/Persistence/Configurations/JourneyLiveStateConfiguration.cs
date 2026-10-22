using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartRail.LiveOperations.Domain.LiveState;

namespace SmartRail.LiveOperations.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="JourneyLiveState"/> to <c>operations.journey_live_states</c>.</summary>
internal sealed class JourneyLiveStateConfiguration : IEntityTypeConfiguration<JourneyLiveState>
{
    public void Configure(EntityTypeBuilder<JourneyLiveState> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("journey_live_states");
        builder.HasKey(state => state.Id);

        builder.Property(state => state.Id).HasColumnName("journey_id").ValueGeneratedNever();
        builder.Property(state => state.ServiceId).HasColumnName("service_id");
        builder.Property(state => state.LineId).HasColumnName("line_id");
        builder.Property(state => state.ServiceDate).HasColumnName("service_date");
        builder.Property(state => state.GuardStaffId).HasColumnName("guard_staff_id");
        builder.Property(state => state.Status).HasColumnName("lifecycle_status").HasConversion(
                status => ToStorage(status),
                value => FromStorage(value)).IsRequired();
        builder.Property(state => state.DelayMinutes).HasColumnName("delay_minutes");
        builder.Property(state => state.LastDepartedStationId).HasColumnName("last_departed_station_id");
        builder.Property(state => state.NextStationId).HasColumnName("next_station_id");
        builder.Property(state => state.LastSequenceNumber).HasColumnName("last_sequence_number");
        builder.Property(state => state.LastUpdatedAt).HasColumnName("last_updated_at");

        builder.HasIndex(state => state.ServiceDate);

        builder
            .HasMany(state => state.Stops)
            .WithOne()
            .HasForeignKey(stop => stop.JourneyId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static string ToStorage(JourneyLifecycleStatus status) => status switch
    {
        JourneyLifecycleStatus.Scheduled => "scheduled",
        JourneyLifecycleStatus.Running => "running",
        JourneyLifecycleStatus.Cancelled => "cancelled",
        JourneyLifecycleStatus.Completed => "completed",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown journey lifecycle status."),
    };

    private static JourneyLifecycleStatus FromStorage(string value) => value switch
    {
        "scheduled" => JourneyLifecycleStatus.Scheduled,
        "running" => JourneyLifecycleStatus.Running,
        "cancelled" => JourneyLifecycleStatus.Cancelled,
        "completed" => JourneyLifecycleStatus.Completed,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown journey lifecycle status."),
    };
}
