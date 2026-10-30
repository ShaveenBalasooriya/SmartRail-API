using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartRail.Audit.Contracts;
using SmartRail.Audit.Domain.Entries;

namespace SmartRail.Audit.Infrastructure.Persistence;

/// <summary>
/// Appends one entry to the single global audit chain inside a transaction the caller already holds
/// (design D2, D3). It is shared by the request-time recorder and the denial consumer, which runs in
/// its own transaction.
/// </summary>
/// <remarks>
/// The advisory lock is what makes positions gap-free: it is held until the transaction ends, so a
/// second append cannot read the same last position. The context must already be pointed at the
/// caller's connection and transaction — the lock and the read would otherwise run outside it.
/// The lock is also what makes the duplicate check safe for a redelivered denial: two deliveries
/// that both read "no entry yet" would otherwise both append one (design D5).
/// </remarks>
internal sealed class AuditChainWriter(TimeProvider timeProvider)
{
    /// <summary>
    /// The fixed key of the chain's advisory lock ("SmartRai"), identical in every append so all
    /// callers queue on the same lock. A different key would serialise against nothing.
    /// </summary>
    private const long ChainLockKey = 0x536D617274526169;

    /// <summary>
    /// Takes the chain's lock, links a new entry to the last one, saves it and returns it. The
    /// transaction is committed by the caller, not here.
    /// </summary>
    /// <param name="auditDb">The audit context, pointed at the caller's connection and transaction.</param>
    /// <param name="request">The action being recorded.</param>
    /// <param name="outcome">Whether the action succeeded or was refused.</param>
    /// <param name="sourceEventId">
    /// The bus event a denial came from. When an entry already carries it, nothing is written and the
    /// result reports a duplicate.
    /// </param>
    /// <param name="occurredAt">
    /// When the action happened; the denial's own attempt time, or the current time when omitted.
    /// </param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    public async Task<AuditAppendResult> AppendAsync(
        AuditDbContext auditDb,
        AuditRecordRequest request,
        AuditOutcome outcome = AuditOutcome.Succeeded,
        Guid? sourceEventId = null,
        DateTimeOffset? occurredAt = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditDb);
        ArgumentNullException.ThrowIfNull(request);

        await auditDb.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock({ChainLockKey})", cancellationToken).ConfigureAwait(false);

        if (sourceEventId is { } eventId
            && await auditDb.Entries
                .AsNoTracking()
                .AnyAsync(entry => entry.SourceEventId == eventId, cancellationToken)
                .ConfigureAwait(false))
        {
            return AuditAppendResult.Duplicate();
        }

        var last = await auditDb.Entries
            .AsNoTracking()
            .OrderByDescending(entry => entry.ChainPosition)
            .Select(entry => new { entry.ChainPosition, entry.EntryHash })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var id = Guid.CreateVersion7();
        var result = AuditEntry.Create(
            id,
            (last?.ChainPosition ?? 0) + 1,
            occurredAt ?? timeProvider.GetUtcNow(),
            request.ActorStaffId,
            request.ActorRole,
            request.Action,
            outcome,
            request.TargetType,
            request.TargetId,
            request.Before,
            request.After,
            request.RelatedEntryId,
            sourceEventId,
            last?.EntryHash ?? AuditEntryHash.GenesisPreviousHash);

        if (result.IsFailure)
        {
            throw new InvalidOperationException(
                $"The audit entry for action '{request.Action}' is invalid: {result.Error.Description}");
        }

        var entry = result.Value;
        auditDb.Entries.Add(entry);

        try
        {
            await auditDb.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (IsDuplicateSourceEvent(exception))
        {
            // The lock above already serialises appends, so this is only a backstop: the unique
            // index on source_event_id says the denial is recorded, which is a duplicate, not a
            // failed delivery the bus should retry.
            auditDb.Entry(entry).State = EntityState.Detached;

            return AuditAppendResult.Duplicate();
        }

        return AuditAppendResult.Appended(id);
    }

    private static bool IsDuplicateSourceEvent(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_entries_source_event_id",
        };
}
