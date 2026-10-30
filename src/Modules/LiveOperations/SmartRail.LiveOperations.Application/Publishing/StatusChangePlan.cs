using SmartRail.LiveOperations.Domain.Changes;

namespace SmartRail.LiveOperations.Application.Publishing;

/// <summary>
/// What one change type's own validation produced for a publication: the row to store, the affected
/// fields the audit entry records, and the entry the audit entry links to when this change corrects
/// another (design D4). Building them here keeps every type's rules next to its handler while the
/// shared pipeline owns the order of the writes.
/// </summary>
/// <param name="Change">The change to store.</param>
/// <param name="Before">The affected fields as they were, as JSON, captured before the fact is applied.</param>
/// <param name="After">
/// The affected fields as the change leaves them, as JSON. A correction evaluates it after the fact
/// sink has recomputed the journey, because the state it reverts is only known then; every other
/// change knows its after values up front and returns a constant.
/// </param>
/// <param name="RelatedEntryId">
/// The audit entry of the change this one corrects, or <see langword="null"/> for every other change.
/// </param>
internal sealed record StatusChangePlan(
    StatusChange Change,
    string? Before,
    Func<string?> After,
    Guid? RelatedEntryId = null);
