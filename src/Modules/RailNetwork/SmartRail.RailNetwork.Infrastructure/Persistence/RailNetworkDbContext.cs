using Microsoft.EntityFrameworkCore;
using SmartRail.RailNetwork.Application.Abstractions;
using SmartRail.RailNetwork.Domain.Entities;
using SmartRail.SharedKernel.Infrastructure.Persistence;

namespace SmartRail.RailNetwork.Infrastructure.Persistence;

/// <summary>
/// The Rail Network module's database context. It is scoped to the <c>network</c> schema, owns its
/// own migrations history table, and is the module's unit of work (design D3). It is also the owner
/// of its event-bus outbox.
/// </summary>
public sealed class RailNetworkDbContext(DbContextOptions<RailNetworkDbContext> options)
    : DbContext(options), IUnitOfWork
{
    public const string Schema = "network";

    public DbSet<Line> Lines => Set<Line>();

    public DbSet<Station> Stations => Set<Station>();

    public DbSet<Platform> Platforms => Set<Platform>();

    public DbSet<LineStation> LineStations => Set<LineStation>();

    public DbSet<Service> Services => Set<Service>();

    public DbSet<ServiceStop> ServiceStops => Set<ServiceStop>();

    public DbSet<Journey> Journeys => Set<Journey>();

    /// <summary>
    /// Opens the transaction that holds the generated journeys and their announcements together, so
    /// a change and its event commit or roll back as one (design D1, D9).
    /// </summary>
    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        var transaction = await Database.BeginTransactionAsync(cancellationToken);
        return new UnitOfWorkTransaction(transaction);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RailNetworkDbContext).Assembly);
        modelBuilder.AddOutbox();
    }
}
