namespace SmartRail.Audit.Contracts;

/// <summary>
/// A successful staff action to append to the audit record (AUD-01, AUD-03). The outcome is
/// always <c>succeeded</c>; a denial is recorded from the access-denied event, not through this
/// port.
/// </summary>
/// <param name="ActorStaffId">The staff member who acted.</param>
/// <param name="ActorRole">The role the staff member acted in at that time.</param>
/// <param name="Action">The name of the action that succeeded.</param>
/// <param name="TargetType">The kind of thing the action changed.</param>
/// <param name="TargetId">The identifier of the thing the action changed.</param>
/// <param name="Before">The value before the change as JSON, or <c>null</c> when there was none.</param>
/// <param name="After">The value after the change as JSON, or <c>null</c> when there is none.</param>
/// <param name="RelatedEntryId">The entry of the change this one corrects, when it is a correction.</param>
public sealed record AuditRecordRequest(
    Guid? ActorStaffId,
    string? ActorRole,
    string Action,
    string? TargetType,
    string? TargetId,
    string? Before,
    string? After,
    Guid? RelatedEntryId);
