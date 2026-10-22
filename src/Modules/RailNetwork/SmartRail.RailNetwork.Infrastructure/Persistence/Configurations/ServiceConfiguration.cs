using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartRail.RailNetwork.Domain.Entities;

namespace SmartRail.RailNetwork.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="Service"/> to <c>network.services</c>.</summary>
internal sealed class ServiceConfiguration : IEntityTypeConfiguration<Service>
{
    public void Configure(EntityTypeBuilder<Service> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("services");
        builder.HasKey(service => service.Id);

        builder.Property(service => service.Id).HasColumnName("id");
        builder.Property(service => service.LineId).HasColumnName("line_id");
        builder.Property(service => service.Direction).HasColumnName("direction").HasConversion(
                direction => direction == ServiceDirection.Inbound ? "inbound" : "outbound",
                value => value == "inbound" ? ServiceDirection.Inbound : ServiceDirection.Outbound).IsRequired();
        builder.Property(service => service.Name).HasColumnName("name").IsRequired();
        builder.Property(service => service.GuardStaffId).HasColumnName("guard_staff_id");

        builder
            .HasOne(service => service.Line)
            .WithMany()
            .HasForeignKey(service => service.LineId);

        builder
            .HasMany(service => service.Stops)
            .WithOne(stop => stop.Service)
            .HasForeignKey(stop => stop.ServiceId);
    }
}
