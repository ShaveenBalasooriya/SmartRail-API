using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartRail.LiveOperations.Domain.LiveState;

namespace SmartRail.LiveOperations.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="JourneyStopLiveState"/> to <c>operations.journey_stop_live_states</c>.</summary>
internal sealed class JourneyStopLiveStateConfiguration : IEntityTypeConfiguration<JourneyStopLiveState>
{
    public void Configure(EntityTypeBuilder<JourneyStopLiveState> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("journey_stop_live_states");
        builder.HasKey(stop => stop.Id);

        builder.Property(stop => stop.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(stop => stop.JourneyId).HasColumnName("journey_id");
        builder.Property(stop => stop.Sequence).HasColumnName("sequence");
        builder.Property(stop => stop.StationId).HasColumnName("station_id");
        builder.Property(stop => stop.IsCalling).HasColumnName("is_calling");
        builder.Property(stop => stop.ScheduledArrival).HasColumnName("scheduled_arrival");
        builder.Property(stop => stop.ScheduledDeparture).HasColumnName("scheduled_departure");
        builder.Property(stop => stop.ExpectedArrival).HasColumnName("expected_arrival");
        builder.Property(stop => stop.ExpectedDeparture).HasColumnName("expected_departure");
        builder.Property(stop => stop.ActualDeparture).HasColumnName("actual_departure");
        builder.Property(stop => stop.PlatformId).HasColumnName("platform_id");
        builder.Property(stop => stop.PlatformChangedAt).HasColumnName("platform_changed_at");

        // One row per stop per journey. It also gives the live-state creation its ON CONFLICT key.
        builder.HasIndex(stop => new { stop.JourneyId, stop.Sequence }).IsUnique();
    }
}
