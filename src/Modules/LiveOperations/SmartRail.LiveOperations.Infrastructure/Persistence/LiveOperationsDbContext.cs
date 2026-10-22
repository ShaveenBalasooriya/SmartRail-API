using Microsoft.EntityFrameworkCore;
using SmartRail.LiveOperations.Application.Abstractions;
using SmartRail.LiveOperations.Domain.LiveState;

namespace SmartRail.LiveOperations.Infrastructure.Persistence;

/// <summary>
/// The Live Operations module's database context. It is scoped to the <c>operations</c> schema and
/// owns its own migrations history table, so no other module's migration state is visible to it
/// (design D2). It is also the module's unit of work.
/// </summary>
public sealed class LiveOperationsDbContext(DbContextOptions<LiveOperationsDbContext> options)
    : DbContext(options), IUnitOfWork
{
    public const string Schema = "operations";

    /// <summary>
    /// The sequence that assigns <c>journey_events.stream_position</c>. Positions can have gaps on
    /// rollback and can commit out of order, so consumers treat them as ordered, not contiguous
    /// (design D3).
    /// </summary>
    public const string StreamPositionSequence = "journey_events_stream_position_seq";

    public DbSet<JourneyLiveState> JourneyLiveStates => Set<JourneyLiveState>();

    public DbSet<JourneyStopLiveState> JourneyStopLiveStates => Set<JourneyStopLiveState>();

    public DbSet<JourneyEvent> JourneyEvents => Set<JourneyEvent>();

    /// <summary>Opens the transaction a handler holds while it locks a journey's live-state row.</summary>
    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        var transaction = await Database.BeginTransactionAsync(cancellationToken);
        return new UnitOfWorkTransaction(transaction);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        => base.SaveChangesAsync(cancellationToken);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.HasSequence<long>(StreamPositionSequence, Schema).StartsAt(1).IncrementsBy(1);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LiveOperationsDbContext).Assembly);
    }
}
