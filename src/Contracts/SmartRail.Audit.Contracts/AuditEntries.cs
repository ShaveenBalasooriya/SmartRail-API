using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartRail.Audit.Contracts;

/// <summary>
/// One entry as the Auditor's list shows it (AUD-01, AUD-02). The list deliberately omits the
/// before and after values and the chain hashes, which only the entry's own view returns.
/// </summary>
/// <param name="Id">The entry's identifier.</param>
/// <param name="ChainPosition">The entry's gap-free position in the single global chain.</param>
/// <param name="OccurredAt">The server time the action occurred.</param>
/// <param name="ActorStaffId">The staff member who acted, or <c>null</c> when it is not known.</param>
/// <param name="ActorRole">The role the staff member acted in at the time.</param>
/// <param name="Action">The action that succeeded or was attempted.</param>
/// <param name="Outcome"><c>succeeded</c> or <c>denied</c>, as the record stores it.</param>
/// <param name="TargetType">The kind of thing the action changed; <c>null</c> for a denial.</param>
/// <param name="TargetId">The identifier of the thing the action changed; <c>null</c> for a denial.</param>
public sealed record AuditEntryListItem(
    Guid Id,
    long ChainPosition,
    DateTimeOffset OccurredAt,
    Guid? ActorStaffId,
    string? ActorRole,
    string Action,
    string Outcome,
    string? TargetType,
    string? TargetId);

/// <summary>
/// One entry in full, including the values it changed and the entry of the change it corrects
/// (AUD-03). It is what <c>GET /audit/entries/{id}</c> returns.
/// </summary>
/// <param name="Id">The entry's identifier.</param>
/// <param name="ChainPosition">The entry's gap-free position in the single global chain.</param>
/// <param name="OccurredAt">The server time the action occurred.</param>
/// <param name="ActorStaffId">The staff member who acted, or <c>null</c> when it is not known.</param>
/// <param name="ActorRole">The role the staff member acted in at the time.</param>
/// <param name="Action">The action that succeeded or was attempted.</param>
/// <param name="Outcome"><c>succeeded</c> or <c>denied</c>, as the record stores it.</param>
/// <param name="TargetType">The kind of thing the action changed; <c>null</c> for a denial.</param>
/// <param name="TargetId">The identifier of the thing the action changed; <c>null</c> for a denial.</param>
/// <param name="Before">The value before the change as JSON, or <c>null</c> when there was none.</param>
/// <param name="After">The value after the change as JSON, or <c>null</c> when there is none.</param>
/// <param name="RelatedEntryId">The entry of the change this entry corrects, when it is a correction.</param>
/// <param name="PreviousHash">The hash of the entry before this one.</param>
/// <param name="EntryHash">The entry's own hash, which the next entry links to.</param>
public sealed record AuditEntryDetail(
    Guid Id,
    long ChainPosition,
    DateTimeOffset OccurredAt,
    Guid? ActorStaffId,
    string? ActorRole,
    string Action,
    string Outcome,
    string? TargetType,
    string? TargetId,
    JsonElement? Before,
    JsonElement? After,
    Guid? RelatedEntryId,
    string PreviousHash,
    string EntryHash);

/// <summary>
/// The chain integrity check's answer (AUD-05). It is what <c>GET /audit/integrity</c> returns: the
/// status as the record's own word rather than an enumeration's number, the number of entries
/// examined, and, only when the chain stopped verifying, the first chain position that failed.
/// </summary>
/// <param name="Status"><c>intact</c> when every entry verified, otherwise <c>broken</c>.</param>
/// <param name="FirstFailingPosition">
/// The first chain position that failed, with the entries examined up to and including it. An intact
/// record omits it from the response rather than sending it as <c>null</c>: the API writes nulls by
/// default, and an intact record must not name a position.
/// </param>
/// <param name="EntriesChecked">How many entries were examined, including a failing one.</param>
public sealed record AuditIntegrityReport(
    string Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? FirstFailingPosition,
    long EntriesChecked);
