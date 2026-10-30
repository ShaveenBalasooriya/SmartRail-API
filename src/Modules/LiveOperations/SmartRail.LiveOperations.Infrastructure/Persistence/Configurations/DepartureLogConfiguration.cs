using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartRail.LiveOperations.Domain.Departures;
using SmartRail.LiveOperations.Domain.LiveState;

namespace SmartRail.LiveOperations.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="DepartureLog"/> to <c>operations.departure_logs</c>.</summary>
internal sealed class DepartureLogConfiguration : IEntityTypeConfiguration<DepartureLog>
{
    public void Configure(EntityTypeBuilder<DepartureLog> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "departure_logs",
            table => table.HasCheckConstraint(
                "CK_departure_logs_delay_cause",
                "delay_cause IN ('signalling','rolling_stock','crew','passenger_boarding',"
                + "'infrastructure','weather','other')"));

        builder.HasKey(log => log.Id);

        // The identifier is generated on the device, so the database never assigns one (GRD-07).
        builder.Property(log => log.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(log => log.JourneyId).HasColumnName("journey_id");
        builder.Property(log => log.StopSequence).HasColumnName("stop_sequence");
        builder.Property(log => log.StationId).HasColumnName("station_id");
        builder.Property(log => log.GuardStaffId).HasColumnName("guard_staff_id");
        builder.Property(log => log.DepartedAt).HasColumnName("departed_at");
        builder.Property(log => log.DelayCause).HasColumnName("delay_cause").HasConversion<string?>(
            cause => cause == null ? null : DelayCauses.ToStorage(cause.Value),
            value => value == null ? null : DelayCauses.FromStorage(value));
        builder.Property(log => log.ReceivedAt).HasColumnName("received_at");
        builder
            .Property(log => log.FactOrder)
            .HasColumnName("fact_order")
            .HasDefaultValueSql(
                $"nextval('{LiveOperationsDbContext.Schema}.{LiveOperationsDbContext.FactOrderSequence}')")
            .ValueGeneratedOnAdd();

        // A stop has at most one departure, so a double tap with two device IDs is settled here
        // rather than recorded twice (GRD-02, departure-logging design D3).
        builder.HasIndex(log => new { log.JourneyId, log.StopSequence }).IsUnique();

        builder
            .HasOne<JourneyLiveState>()
            .WithMany()
            .HasForeignKey(log => log.JourneyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
