using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartRail.RailNetwork.Domain.Entities;
using SmartRail.RailNetwork.Domain.ValueObjects;

namespace SmartRail.RailNetwork.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="Platform"/> to <c>network.platforms</c>.</summary>
internal sealed class PlatformConfiguration : IEntityTypeConfiguration<Platform>
{
    public void Configure(EntityTypeBuilder<Platform> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("platforms");
        builder.HasKey(platform => platform.Id);

        builder.Property(platform => platform.Id).HasColumnName("id");
        builder.Property(platform => platform.StationId).HasColumnName("station_id");
        // The value object is converted to its stored string, so the column stays unchanged.
        builder.Property(platform => platform.Number)
            .HasColumnName("number")
            .HasConversion(number => number.Value, value => PlatformNumber.FromStorage(value))
            .IsRequired();

        builder.HasIndex(platform => new { platform.StationId, platform.Number }).IsUnique();

        builder
            .HasOne(platform => platform.Station)
            .WithMany()
            .HasForeignKey(platform => platform.StationId);
    }
}
