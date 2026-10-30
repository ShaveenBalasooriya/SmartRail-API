using Microsoft.EntityFrameworkCore;
using SmartRail.Audit.Contracts;
using SmartRail.Audit.Domain.Entries;
using SmartRail.Audit.Infrastructure.Persistence;
using SmartRail.IdentityAccess.Contracts;
using SmartRail.SharedKernel.Application.Events;

namespace SmartRail.Audit.Infrastructure;

/// <summary>
/// Records every refused write Identity announces (design D5). The denied request has no business
/// transaction of its own, so this opens one on the audit context and commits the entry itself; the
/// entry therefore appears shortly after the denial rather than in the same instant, and a failed
/// write is retried by the bus instead of losing the denial.
/// </summary>
/// <remarks>
/// Delivery is at least once, so the writer treats an event whose ID already has an entry as a
/// duplicate. The entry carries no passenger data, no token and no client address: only the staff
/// member, the role they had, the action they attempted and the time (AUD-07, M-03).
/// </remarks>
internal sealed class AccessDeniedConsumer(AuditDbContext auditDb, AuditChainWriter chainWriter)
    : IEventConsumer<AccessDenied>
{
    /// <inheritdoc />
    public string Name => "audit.access-denied";

    /// <inheritdoc />
    public async Task Handle(AccessDenied integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        await using var transaction = await auditDb.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var result = await chainWriter.AppendAsync(
            auditDb,
            new AuditRecordRequest(
                integrationEvent.StaffId,
                integrationEvent.Role,
                integrationEvent.Action,
                TargetType: null,
                TargetId: null,
                Before: null,
                After: null,
                RelatedEntryId: null),
            AuditOutcome.Denied,
            integrationEvent.EventId,
            integrationEvent.AttemptedAt,
            cancellationToken).ConfigureAwait(false);

        // A redelivery wrote nothing, so its transaction is rolled back rather than committed
        // empty; a unique violation leaves that transaction unusable, which is another reason not
        // to commit it.
        if (!result.Written)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
