using Microsoft.EntityFrameworkCore.Storage;
using SmartRail.RailNetwork.Application.Abstractions;

namespace SmartRail.RailNetwork.Infrastructure.Persistence;

/// <summary>Adapts an Entity Framework transaction to the module's unit-of-work port.</summary>
internal sealed class UnitOfWorkTransaction(IDbContextTransaction transaction) : IUnitOfWorkTransaction
{
    public Task CommitAsync(CancellationToken cancellationToken = default)
        => transaction.CommitAsync(cancellationToken);

    public ValueTask DisposeAsync() => transaction.DisposeAsync();
}
