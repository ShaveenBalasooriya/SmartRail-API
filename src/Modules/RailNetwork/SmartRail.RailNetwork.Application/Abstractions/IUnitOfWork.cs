namespace SmartRail.RailNetwork.Application.Abstractions;

/// <summary>
/// The module's unit of work. It is implemented by the module's <c>DbContext</c> so the application
/// layer commits without knowing about EF Core (design D3).
/// </summary>
public interface IUnitOfWork
{
    /// <summary>Opens a transaction on the module's connection.</summary>
    Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>Commits every change made in this scope.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// A transaction the handler commits explicitly, so the generated journeys and the outbox rows that
/// announce them share one commit (design D1, D9).
/// </summary>
public interface IUnitOfWorkTransaction : IAsyncDisposable
{
    /// <summary>Commits every change written inside the transaction.</summary>
    Task CommitAsync(CancellationToken cancellationToken = default);
}
