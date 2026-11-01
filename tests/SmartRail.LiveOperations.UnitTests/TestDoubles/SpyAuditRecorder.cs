using SmartRail.Audit.Contracts;

namespace SmartRail.LiveOperations.UnitTests.TestDoubles;

/// <summary>Records audit requests and can simulate an audit dependency failure.</summary>
internal sealed class SpyAuditRecorder : IAuditRecorder
{
    private readonly Guid _auditEntryId = Guid.CreateVersion7();

    public List<AuditRecordRequest> Requests { get; } = [];

    public Exception? ExceptionToThrow { get; set; }

    public Guid AuditEntryId => _auditEntryId;

    public Task<Guid> RecordAsync(
        AuditRecordRequest request,
        CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        return Task.FromResult(_auditEntryId);
    }
}
