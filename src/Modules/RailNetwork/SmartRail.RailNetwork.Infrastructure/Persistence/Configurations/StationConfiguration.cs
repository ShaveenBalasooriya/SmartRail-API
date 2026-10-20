using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartRail.RailNetwork.Domain.Entities;

namespace SmartRail.RailNetwork.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="Station"/> to <c>network.stations</c>.</summary>
internal sealed class StationConfiguration : IEntityTypeConfiguration<Station>
{
    public void Configure(EntityTypeBuilder<Station> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("stations");
        builder.HasKey(station => station.Id);

        builder.Property(station => station.Id).HasColumnName("id");
        builder.Property(station => station.Code).HasColumnName("code").IsRequired();
        builder.Property(station => station.Name).HasColumnName("name").IsRequired();

        builder.HasIndex(station => station.Code).IsUnique();
    }
}
