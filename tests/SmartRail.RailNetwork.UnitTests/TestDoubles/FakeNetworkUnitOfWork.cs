using SmartRail.RailNetwork.Application.Abstractions;

namespace SmartRail.RailNetwork.UnitTests.TestDoubles;

/// <summary>
/// Stands in for the module's DbContext as a unit of work. It counts the calls and lets a fake
/// repository undo its uncommitted writes, so a rollback behaves like a database transaction being
/// rolled back: the inserted rows and the outbox rows recorded in that transaction both disappear.
/// </summary>
internal sealed class FakeNetworkUnitOfWork : IUnitOfWork
{
    private readonly List<Action> _rollbackActions = [];
    private readonly List<Action> _afterCommitActions = [];

    public int BeginTransactionCount { get; private set; }

    public int SaveChangesCount { get; private set; }

    public int CommitCount { get; private set; }

    public int RollbackCount { get; private set; }

    /// <summary>When set, saving fails the way a database error does, after the inserts were staged.</summary>
    public Exception? ExceptionOnSave { get; set; }

    internal void RegisterRollback(Action rollback) => _rollbackActions.Add(rollback);

    internal void RegisterAfterCommit(Action afterCommit) => _afterCommitActions.Add(afterCommit);

    public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        BeginTransactionCount++;
        return Task.FromResult<IUnitOfWorkTransaction>(new Transaction(this));
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCount++;

        if (ExceptionOnSave is not null)
        {
            throw ExceptionOnSave;
        }

        return Task.FromResult(0);
    }

    private void Commit()
    {
        CommitCount++;

        foreach (var action in _afterCommitActions)
        {
            action();
        }

        _afterCommitActions.Clear();
        _rollbackActions.Clear();
    }

    private void Rollback()
    {
        RollbackCount++;

        for (var index = _rollbackActions.Count - 1; index >= 0; index--)
        {
            _rollbackActions[index]();
        }

        _rollbackActions.Clear();
        _afterCommitActions.Clear();
    }

    private sealed class Transaction(FakeNetworkUnitOfWork owner) : IUnitOfWorkTransaction
    {
        private bool _committed;

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            _committed = true;
            owner.Commit();
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            if (!_committed)
            {
                owner.Rollback();
            }

            return ValueTask.CompletedTask;
        }
    }
}
