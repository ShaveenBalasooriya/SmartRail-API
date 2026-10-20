using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartRail.RailNetwork.Domain.Entities;
using SmartRail.RailNetwork.Domain.ValueObjects;

namespace SmartRail.RailNetwork.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="Line"/> to <c>network.lines</c>.</summary>
internal sealed class LineConfiguration : IEntityTypeConfiguration<Line>
{
    public void Configure(EntityTypeBuilder<Line> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("lines");
        builder.HasKey(line => line.Id);

        builder.Property(line => line.Id).HasColumnName("id");
        // The value object is converted to its stored string, so the column stays unchanged.
        builder.Property(line => line.Code)
            .HasColumnName("code")
            .HasConversion(code => code.Value, value => LineCode.FromStorage(value))
            .IsRequired();
        builder.Property(line => line.Name).HasColumnName("name").IsRequired();

        builder.HasIndex(line => line.Code).IsUnique();
    }
}
