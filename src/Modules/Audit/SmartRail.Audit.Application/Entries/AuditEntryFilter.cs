using SmartRail.Audit.Domain.Entries;

namespace SmartRail.Audit.Application.Entries;

/// <summary>
/// The filters the Auditor's list accepts (audit-record spec: actor, time range and outcome). A
/// missing value is no filter, which is why a denial's unknown actor cannot be selected by leaving
/// the actor out.
/// </summary>
/// <param name="ActorStaffId">Only entries of this staff member.</param>
/// <param name="From">Only entries at or after this time, inclusive.</param>
/// <param name="To">Only entries at or before this time, inclusive.</param>
/// <param name="Outcome">Only entries with this outcome.</param>
public sealed record AuditEntryFilter(
    Guid? ActorStaffId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    AuditOutcome? Outcome);
