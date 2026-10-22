namespace SmartRail.LiveOperations.Application.Abstractions;

/// <summary>
/// The Live Operations module's unit of work. The module's <c>DbContext</c> implements it, so a
/// handler can open the transaction that holds the live-state row lock, save its changes and commit
/// without referencing Entity Framework (design D4).
/// </summary>
public interface IUnitOfWork
{
    /// <summary>Opens a transaction on the module's connection.</summary>
    Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>Writes every pending change in the current transaction.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>A transaction the handler commits explicitly, so it can publish only after the commit.</summary>
public interface IUnitOfWorkTransaction : IAsyncDisposable
{
    /// <summary>Commits every change written inside the transaction.</summary>
    Task CommitAsync(CancellationToken cancellationToken = default);
}
