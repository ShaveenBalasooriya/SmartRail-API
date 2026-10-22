using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartRail.RailNetwork.Domain.Entities;

namespace SmartRail.RailNetwork.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="ServiceStop"/> to <c>network.service_stops</c>.</summary>
internal sealed class ServiceStopConfiguration : IEntityTypeConfiguration<ServiceStop>
{
    public void Configure(EntityTypeBuilder<ServiceStop> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("service_stops");
        builder.HasKey(stop => stop.Id);

        builder.Property(stop => stop.Id).HasColumnName("id");
        builder.Property(stop => stop.ServiceId).HasColumnName("service_id");
        builder.Property(stop => stop.StationId).HasColumnName("station_id");
        builder.Property(stop => stop.Sequence).HasColumnName("sequence");
        builder.Property(stop => stop.IsCalling).HasColumnName("is_calling");
        builder.Property(stop => stop.ScheduledArrival).HasColumnName("scheduled_arrival");
        builder.Property(stop => stop.ScheduledDeparture).HasColumnName("scheduled_departure");
        builder.Property(stop => stop.PlatformId).HasColumnName("platform_id");

        builder.HasIndex(stop => new { stop.ServiceId, stop.Sequence }).IsUnique();

        builder
            .HasOne(stop => stop.Station)
            .WithMany()
            .HasForeignKey(stop => stop.StationId);

        builder
            .HasOne(stop => stop.Platform)
            .WithMany()
            .HasForeignKey(stop => stop.PlatformId);
    }
}
