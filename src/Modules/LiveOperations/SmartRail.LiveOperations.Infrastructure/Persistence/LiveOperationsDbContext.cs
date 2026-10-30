using Microsoft.EntityFrameworkCore;
using SmartRail.LiveOperations.Application.Abstractions;
using SmartRail.LiveOperations.Domain.Changes;
using SmartRail.LiveOperations.Domain.Departures;
using SmartRail.LiveOperations.Domain.LiveState;
using SmartRail.SharedKernel.Infrastructure.Persistence;

namespace SmartRail.LiveOperations.Infrastructure.Persistence;

/// <summary>
/// The Live Operations module's database context. It is scoped to the <c>operations</c> schema and
/// owns its own migrations history table, so no other module's migration state is visible to it
/// (design D2). It is also the module's unit of work and the owner of its event-bus outbox.
/// </summary>
public sealed class LiveOperationsDbContext(
    DbContextOptions<LiveOperationsDbContext> options,
    IRequestTransaction? requestTransaction = null)
    : DbContext(options), IUnitOfWork
{
    public const string Schema = "operations";

    /// <summary>
    /// The sequence that assigns <c>journey_events.stream_position</c>. Positions can have gaps on
    /// rollback and can commit out of order, so consumers treat them as ordered, not contiguous
    /// (design D3).
    /// </summary>
    public const string StreamPositionSequence = "journey_events_stream_position_seq";

    /// <summary>
    /// The sequence that assigns the commit order of a recorded fact, shared by every fact table so
    /// facts from <c>status_changes</c> and <c>departure_logs</c> are ordered against each other
    /// (controller-operations package decision D-6).
    /// </summary>
    public const string FactOrderSequence = "fact_order_seq";

    public DbSet<JourneyLiveState> JourneyLiveStates => Set<JourneyLiveState>();

    public DbSet<JourneyStopLiveState> JourneyStopLiveStates => Set<JourneyStopLiveState>();

    public DbSet<JourneyEvent> JourneyEvents => Set<JourneyEvent>();

    public DbSet<StatusChange> StatusChanges => Set<StatusChange>();

    public DbSet<DepartureLog> DepartureLogs => Set<DepartureLog>();

    /// <summary>
    /// Opens the transaction a handler holds while it locks a journey's live-state row. Inside a
    /// state-changing request it joins the request's transaction instead of opening its own
    /// (controller-operations design D2, audit design D1), so the row lock is taken on the request's
    /// connection and the change commits with the audit entry the Host commits once.
    /// </summary>
    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (requestTransaction is { IsActive: true })
        {
            // Enlist before the handler runs any raw SQL: the row lock must be taken on the
            // request's connection and inside its transaction, or it would lock nothing.
            await requestTransaction.EnlistAsync(this, cancellationToken);

            return JoinedRequestTransaction.Instance;
        }

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
        modelBuilder.HasSequence<long>(FactOrderSequence, Schema).StartsAt(1).IncrementsBy(1);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LiveOperationsDbContext).Assembly);
        modelBuilder.AddOutbox();
    }
}
