using SmartRail.Audit.Contracts;
using SmartRail.Audit.Infrastructure.Persistence;
using SmartRail.SharedKernel.Infrastructure.Persistence;

namespace SmartRail.Audit.Infrastructure;

/// <summary>
/// Records a staff member's state change through the shared request transaction (design D1, D2,
/// D3). The entry commits with the caller's change or rolls back with it.
/// </summary>
internal sealed class AuditRecorder(
    AuditDbContext auditDb,
    IRequestTransaction requestTransaction,
    AuditChainWriter chainWriter) : IAuditRecorder
{
    /// <inheritdoc />
    public async Task<Guid> RecordAsync(
        AuditRecordRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!requestTransaction.IsActive)
        {
            throw new InvalidOperationException(
                "An audit entry can only be recorded inside the transaction of a state-changing "
                + "request. Without it the entry would not commit together with the change it "
                + "describes, so the change must not proceed unaudited.");
        }

        // Enlist before the writer takes the advisory lock and reads the last entry: those must run
        // on the request's connection and inside its transaction, or they would not protect the
        // chain position.
        await requestTransaction.EnlistAsync(auditDb, cancellationToken).ConfigureAwait(false);

        return (await chainWriter.AppendAsync(auditDb, request, cancellationToken: cancellationToken)
            .ConfigureAwait(false)).EntryId;
    }
}
