using SmartRail.Audit.Contracts;

namespace SmartRail.Host.IntegrationTests.Flows.Support;

/// <summary>
/// An audit recorder that fails at the last step of a publishing pipeline, after the change, its
/// journey event and its outbox row were written. A flow uses it to prove that the entry commits with
/// the change or the change does not happen at all.
/// </summary>
internal sealed class ThrowingAuditRecorder : IAuditRecorder
{
    /// <inheritdoc />
    public Task<Guid> RecordAsync(
        AuditRecordRequest request,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Injected audit failure after the change was written.");
}
