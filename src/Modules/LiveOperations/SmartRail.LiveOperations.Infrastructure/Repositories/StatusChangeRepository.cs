using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartRail.LiveOperations.Application.Publishing;
using SmartRail.LiveOperations.Domain.Changes;
using SmartRail.LiveOperations.Infrastructure.Persistence;

namespace SmartRail.LiveOperations.Infrastructure.Repositories;

/// <summary>
/// Reads and writes <c>operations.status_changes</c>. A published change is added to the module's
/// unit of work, so the publishing pipeline's save writes it in the request's transaction and the
/// database assigns its commit order (design D2, D-6); the reads are tracked for the same reason,
/// because the correction pipeline acts on the original in that transaction.
/// </summary>
internal sealed class StatusChangeRepository(LiveOperationsDbContext database) : IStatusChangeRepository
{
    /// <summary>The unique index that lets a change be corrected at most once.</summary>
    private const string CorrectionIndex = "IX_status_changes_corrects_change_id";

    /// <summary>The savepoint a refused correction rolls back to, keeping the request transaction usable.</summary>
    private const string CorrectionSavepoint = "status_change_correction";

    public async Task<bool> AddAsync(StatusChange change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);

        database.StatusChanges.Add(change);

        // Every other change type has no unique constraint that can refuse it, so it is written
        // exactly as before.
        if (change.Type != StatusChangeType.Correction)
        {
            await database.SaveChangesAsync(cancellationToken);
            return true;
        }

        var transaction = database.Database.CurrentTransaction
            ?? throw new InvalidOperationException(
                "A correction must be written inside the publishing transaction.");

        await transaction.CreateSavepointAsync(CorrectionSavepoint, cancellationToken);

        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (IsDuplicateCorrection(exception))
        {
            // The row lock already orders two corrections of one change, so this is only a backstop:
            // the unique index says the original is withdrawn, which is a conflict, not a bug. The
            // savepoint keeps the request transaction usable for the 409 response.
            await transaction.RollbackToSavepointAsync(CorrectionSavepoint, cancellationToken);
            database.Entry(change).State = EntityState.Detached;
            return false;
        }
    }

    public Task<StatusChange?> GetAsync(Guid changeId, CancellationToken cancellationToken = default) =>
        database.StatusChanges.FirstOrDefaultAsync(change => change.Id == changeId, cancellationToken);

    public Task<bool> IsCorrectedAsync(Guid changeId, CancellationToken cancellationToken = default) =>
        database.StatusChanges.AnyAsync(change => change.CorrectsChangeId == changeId, cancellationToken);

    private static bool IsDuplicateCorrection(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: CorrectionIndex,
        };
}
