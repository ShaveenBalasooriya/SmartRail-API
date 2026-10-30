namespace SmartRail.IdentityAccess.Contracts;

/// <summary>
/// Announced when a staff request is refused with 403 on a state-changing endpoint (design D6).
/// Audit consumes it and appends it to the audit record; until that change lands the event simply
/// has no consumer. Reads are never announced.
/// </summary>
/// <param name="EventId">
/// The event's own identifier, which the publisher also passes to the outbox. The bus delivers only
/// the payload, so this is what a consumer keys its idempotency on: a redelivery carries the same
/// value and must not add a second audit entry (audit design D5).
/// </param>
/// <param name="StaffId">The caller's SmartRail account, when the token resolved to one.</param>
/// <param name="Role">The caller's resolved role, when it had exactly one catalogue group.</param>
/// <param name="Action">The endpoint's name, or the method and route template when it has none.</param>
/// <param name="AttemptedAt">The server time of the refusal.</param>
public sealed record AccessDenied(
    Guid EventId,
    Guid? StaffId,
    string? Role,
    string Action,
    DateTimeOffset AttemptedAt);
