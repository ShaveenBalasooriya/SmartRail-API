using Microsoft.EntityFrameworkCore;
using SmartRail.RailNetwork.Domain.Entities;

namespace SmartRail.RailNetwork.Infrastructure.Persistence;

/// <summary>
/// The Rail Network module's database context. It is scoped to the <c>network</c> schema and owns its own migrations history table, so no other module's migration state is
/// visible to it (design D2).
/// </summary>
public sealed class RailNetworkDbContext(DbContextOptions<RailNetworkDbContext> options) : DbContext(options)
{
    public const string Schema = "network";

    public DbSet<Line> Lines => Set<Line>();

    public DbSet<Station> Stations => Set<Station>();

    public DbSet<Platform> Platforms => Set<Platform>();

    public DbSet<LineStation> LineStations => Set<LineStation>();

    public DbSet<Service> Services => Set<Service>();

    public DbSet<ServiceStop> ServiceStops => Set<ServiceStop>();

    public DbSet<Journey> Journeys => Set<Journey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RailNetworkDbContext).Assembly);
    }
}
