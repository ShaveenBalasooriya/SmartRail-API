namespace SmartRail.Audit.Contracts;

/// <summary>
/// Appends a staff member's successful state change to the audit record (design D1, D2). A module
/// records through this interface only, so the write can later move onto the event bus without
/// changing the caller.
/// </summary>
public interface IAuditRecorder
{
    /// <summary>
    /// Records a successful staff action in the caller's request transaction and returns the new
    /// entry's ID.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no request transaction is active: the entry would otherwise commit separately
    /// from the change it describes, or not at all.
    /// </exception>
    Task<Guid> RecordAsync(AuditRecordRequest request, CancellationToken cancellationToken = default);
}
