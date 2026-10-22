using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartRail.RailNetwork.Domain.Entities;

namespace SmartRail.RailNetwork.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="Journey"/> to <c>network.journeys</c>.</summary>
internal sealed class JourneyConfiguration : IEntityTypeConfiguration<Journey>
{
    public void Configure(EntityTypeBuilder<Journey> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("journeys");
        builder.HasKey(journey => journey.Id);

        builder.Property(journey => journey.Id).HasColumnName("id");
        builder.Property(journey => journey.ServiceId).HasColumnName("service_id");
        builder.Property(journey => journey.ServiceDate).HasColumnName("service_date");
        builder.Property(journey => journey.ScheduledDepartureAt).HasColumnName("scheduled_departure_at");
        builder.Property(journey => journey.CreatedAt).HasColumnName("created_at");

        builder.HasIndex(journey => new { journey.ServiceId, journey.ServiceDate }).IsUnique();

        builder
            .HasOne(journey => journey.Service)
            .WithMany()
            .HasForeignKey(journey => journey.ServiceId);
    }
}
