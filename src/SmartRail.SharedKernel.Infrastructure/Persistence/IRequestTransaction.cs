using Microsoft.EntityFrameworkCore;

namespace SmartRail.SharedKernel.Infrastructure.Persistence;

/// <summary>
/// The one database transaction a state-changing request runs in (design D1, D3). Every module's
/// <c>DbContext</c> is enlisted in it before its first save, so a change in one module and the audit
/// entry another module appends commit or roll back together.
/// </summary>
/// <remarks>
/// The transaction is scoped to the request and opened lazily: a request that never writes never
/// opens a connection or a transaction. When no request transaction was begun, enlistment is a
/// no-op, which is what keeps migrations, seeding, the event dispatcher and the authorisation
/// result handler outside it and behaving as before.
/// </remarks>
public interface IRequestTransaction : IAsyncDisposable
{
    /// <summary>True once the transaction has begun and until it has been committed or rolled back.</summary>
    bool IsActive { get; }

    /// <summary>
    /// Opens the request's connection and starts its transaction. The first call does the work; a
    /// later call on the same request is harmless.
    /// </summary>
    Task BeginAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Points a context at the request's connection and transaction before it is used to write.
    /// </summary>
    /// <remarks>
    /// A context is enlisted automatically just before it saves, but code that queries with raw SQL
    /// or reads before saving must enlist first, or it would run on another connection and outside
    /// the transaction. A no-op when no transaction was begun, and harmless when the context is
    /// already enlisted.
    /// </remarks>
    Task EnlistAsync(DbContext context, CancellationToken cancellationToken = default);

    /// <summary>Commits the transaction. Does nothing when no transaction was begun.</summary>
    Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>Rolls the transaction back. Does nothing when no transaction was begun.</summary>
    Task RollbackAsync(CancellationToken cancellationToken = default);
}
