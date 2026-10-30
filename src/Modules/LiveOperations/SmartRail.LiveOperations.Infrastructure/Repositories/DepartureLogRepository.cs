using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartRail.LiveOperations.Application.Departures;
using SmartRail.LiveOperations.Domain.Departures;
using SmartRail.LiveOperations.Infrastructure.Persistence;

namespace SmartRail.LiveOperations.Infrastructure.Repositories;

/// <summary>
/// Reads and writes <c>operations.departure_logs</c>. A log is added to the module's unit of work, so
/// the request's save writes it in the request's transaction and the database assigns its commit
/// order from the shared fact sequence (controller-operations package decision D-6). The read by
/// identifier is tracked, because the logging use case compares a replay with the stored row in that
/// same transaction; the read for the journey is not.
/// </summary>
internal sealed class DepartureLogRepository(LiveOperationsDbContext database) : IDepartureLogRepository
{
    /// <summary>The unique index that allows one departure per stop of a journey.</summary>
    private const string StopIndex = "IX_departure_logs_journey_id_stop_sequence";

    /// <summary>The savepoint a refused duplicate rolls back to, keeping the request transaction usable.</summary>
    private const string LogSavepoint = "departure_log";

    public Task<DepartureLog?> GetAsync(Guid logId, CancellationToken cancellationToken = default) =>
        database.DepartureLogs.FirstOrDefaultAsync(log => log.Id == logId, cancellationToken);

    public async Task<IReadOnlyList<DepartureLog>> GetForJourneyAsync(
        Guid journeyId,
        CancellationToken cancellationToken = default) =>
        await database.DepartureLogs
            .AsNoTracking()
            .Where(log => log.JourneyId == journeyId)
            .OrderBy(log => log.StopSequence)
            .ToListAsync(cancellationToken);

    public async Task<bool> AddAsync(DepartureLog log, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(log);

        database.DepartureLogs.Add(log);

        var transaction = database.Database.CurrentTransaction
            ?? throw new InvalidOperationException(
                "A departure log must be written inside the logging transaction.");

        await transaction.CreateSavepointAsync(LogSavepoint, cancellationToken);

        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (IsDuplicateStop(exception))
        {
            // The stop already has a departure, which is a conflict rather than a bug: the unique
            // index is what settles two logs with different device IDs. Rolling back to the
            // savepoint keeps the request transaction usable for the 409 response.
            await transaction.RollbackToSavepointAsync(LogSavepoint, cancellationToken);
            database.Entry(log).State = EntityState.Detached;
            return false;
        }
    }

    private static bool IsDuplicateStop(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: StopIndex,
        };
}
