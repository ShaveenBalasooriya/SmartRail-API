using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace SmartRail.SharedKernel.Infrastructure.Persistence;

/// <summary>
/// The scoped request transaction (design D1, D3). It owns one <see cref="NpgsqlConnection"/> and
/// one <see cref="NpgsqlTransaction"/> for the request and points every module
/// <see cref="DbContext"/> at them before it saves.
/// </summary>
/// <remarks>
/// EF Core never commits a transaction it did not start (<c>BatchExecutor</c> commits only the
/// transaction it began), so once a context has been pointed at this transaction with
/// <see cref="DatabaseFacade.UseTransaction(DbTransaction)"/> its save simply joins it. The
/// connection is set first because the provider rejects a transaction whose connection is not the
/// context's connection.
/// </remarks>
internal sealed class RequestTransaction(string connectionString) : IRequestTransaction, IDisposable
{
    private readonly HashSet<DbContext> _enlisted = new(ReferenceEqualityComparer.Instance);
    private NpgsqlConnection? _connection;
    private NpgsqlTransaction? _transaction;
    private bool _begun;
    private bool _completed;

    /// <inheritdoc />
    public bool IsActive => _begun && !_completed;

    /// <inheritdoc />
    public async Task BeginAsync(CancellationToken cancellationToken = default)
    {
        if (_begun)
        {
            return;
        }

        var connection = new NpgsqlConnection(connectionString);

        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            _transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        _connection = connection;
        _begun = true;
    }

    /// <inheritdoc />
    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (!IsActive)
        {
            return;
        }

        await _transaction!.CommitAsync(cancellationToken).ConfigureAwait(false);
        _completed = true;
    }

    /// <inheritdoc />
    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (!IsActive)
        {
            return;
        }

        await _transaction!.RollbackAsync(cancellationToken).ConfigureAwait(false);
        _completed = true;
    }

    /// <summary>
    /// Points a context at this request's connection and transaction. It is a no-op when no request
    /// transaction was begun, and a no-op for a context that is already enlisted.
    /// </summary>
    internal void Enlist(DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!IsActive || !_enlisted.Add(context))
        {
            return;
        }

        try
        {
            EnsureCanEnlist(context);

            if (context.Database.GetDbConnection().State != ConnectionState.Closed)
            {
                context.Database.CloseConnection();
            }

            context.Database.SetDbConnection(_connection);

            if (context.Database.CurrentTransaction is null)
            {
                context.Database.UseTransaction(_transaction);
            }
        }
        catch
        {
            _enlisted.Remove(context);
            throw;
        }
    }

    /// <summary>
    /// Points a context at this request's connection and transaction. It is a no-op when no request
    /// transaction was begun, and a no-op for a context that is already enlisted.
    /// </summary>
    public async Task EnlistAsync(DbContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!IsActive || !_enlisted.Add(context))
        {
            return;
        }

        try
        {
            EnsureCanEnlist(context);

            if (context.Database.GetDbConnection().State != ConnectionState.Closed)
            {
                await context.Database.CloseConnectionAsync().ConfigureAwait(false);
            }

            context.Database.SetDbConnection(_connection);

            if (context.Database.CurrentTransaction is null)
            {
                await context.Database.UseTransactionAsync(_transaction, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            _enlisted.Remove(context);
            throw;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
        {
            await _transaction.DisposeAsync().ConfigureAwait(false);
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }

        Clear();
    }

    /// <summary>
    /// Synchronous disposal, which a scope disposed with <c>using</c> calls (the migrator and seeder
    /// scopes do). An uncommitted transaction rolls back when it is disposed.
    /// </summary>
    public void Dispose()
    {
        _transaction?.Dispose();
        _connection?.Dispose();
        Clear();
    }

    private void Clear()
    {
        _transaction = null;
        _connection = null;
        _enlisted.Clear();
        _begun = false;
        _completed = true;
    }

    /// <summary>
    /// A context that already opened its own transaction cannot also join the request's one; joining
    /// would silently leave its writes outside the transaction the request commits.
    /// </summary>
    private static void EnsureCanEnlist(DbContext context)
    {
        if (context.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException(
                $"The {context.GetType().Name} already has its own database transaction and cannot "
                + "enlist in the request transaction.");
        }
    }
}
