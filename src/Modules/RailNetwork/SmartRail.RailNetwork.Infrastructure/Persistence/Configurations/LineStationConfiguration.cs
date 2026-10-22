using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartRail.RailNetwork.Domain.Entities;

namespace SmartRail.RailNetwork.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="LineStation"/> to <c>network.line_stations</c>.</summary>
internal sealed class LineStationConfiguration : IEntityTypeConfiguration<LineStation>
{
    public void Configure(EntityTypeBuilder<LineStation> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("line_stations");
        builder.HasKey(lineStation => lineStation.Id);

        builder.Property(lineStation => lineStation.Id).HasColumnName("id");
        builder.Property(lineStation => lineStation.LineId).HasColumnName("line_id");
        builder.Property(lineStation => lineStation.StationId).HasColumnName("station_id");
        builder.Property(lineStation => lineStation.Sequence).HasColumnName("sequence");

        // A station appears at most once on a line, and each position on a line is used once.
        builder.HasIndex(lineStation => new { lineStation.LineId, lineStation.StationId }).IsUnique();
        builder.HasIndex(lineStation => new { lineStation.LineId, lineStation.Sequence }).IsUnique();

        builder
            .HasOne(lineStation => lineStation.Line)
            .WithMany(line => line.LineStations)
            .HasForeignKey(lineStation => lineStation.LineId);

        builder
            .HasOne(lineStation => lineStation.Station)
            .WithMany()
            .HasForeignKey(lineStation => lineStation.StationId);
    }
}
