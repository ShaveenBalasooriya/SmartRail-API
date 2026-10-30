using Microsoft.EntityFrameworkCore.Storage;
using SmartRail.LiveOperations.Application.Abstractions;

namespace SmartRail.LiveOperations.Infrastructure.Persistence;

/// <summary>Adapts an Entity Framework transaction to the module's unit-of-work port.</summary>
internal sealed class UnitOfWorkTransaction(IDbContextTransaction transaction) : IUnitOfWorkTransaction
{
    public Task CommitAsync(CancellationToken cancellationToken = default)
        => transaction.CommitAsync(cancellationToken);

    public ValueTask DisposeAsync() => transaction.DisposeAsync();
}

/// <summary>
/// The transaction a handler holds when it runs inside the request's own transaction
/// (controller-operations design D2). The Host's middleware commits the request once, or rolls it
/// back when the request throws or answers 5xx, so the handler's commit and dispose do nothing.
/// </summary>
internal sealed class JoinedRequestTransaction : IUnitOfWorkTransaction
{
    /// <summary>The shared instance; it holds no state.</summary>
    public static JoinedRequestTransaction Instance { get; } = new();

    private JoinedRequestTransaction()
    {
    }

    public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
