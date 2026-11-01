using SmartRail.LiveOperations.Application.Abstractions;

namespace SmartRail.LiveOperations.UnitTests.TestDoubles;

/// <summary>
/// An in-memory unit of work. Rollback actions let the fake repositories model the atomic request
/// transaction without a database: disposing an uncommitted transaction undoes their pending writes.
/// </summary>
internal sealed class FakeUnitOfWork : IUnitOfWork
{
    private readonly List<Action> _rollbackActions = [];

    public int BeginTransactionCount { get; private set; }

    public int SaveChangesCount { get; private set; }

    public int CommitCount { get; private set; }

    public int RollbackCount { get; private set; }

    public Task<IUnitOfWorkTransaction> BeginTransactionAsync(
        CancellationToken cancellationToken = default)
    {
        BeginTransactionCount++;
        return Task.FromResult<IUnitOfWorkTransaction>(new FakeUnitOfWorkTransaction(this));
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCount++;
        return Task.FromResult(0);
    }

    internal void RegisterRollback(Action rollback) => _rollbackActions.Add(rollback);

    private void Commit()
    {
        CommitCount++;
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
    }

    private sealed class FakeUnitOfWorkTransaction(FakeUnitOfWork owner) : IUnitOfWorkTransaction
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
