using MediatR;

namespace SmartRail.IdentityAccess.Contracts;

/// <summary>
/// Announced when a staff request is refused with 403 on a state-changing endpoint (design D6).
/// Audit subscribes and appends it to the audit record; until that change lands the notification
/// simply has no handler. Reads are never announced.
/// </summary>
/// <param name="StaffId">The caller's SmartRail account, when the token resolved to one.</param>
/// <param name="Role">The caller's resolved role, when it had exactly one catalogue group.</param>
/// <param name="Action">The endpoint's name, or the method and route template when it has none.</param>
/// <param name="AttemptedAt">The server time of the refusal.</param>
public sealed record AccessDenied(
    Guid? StaffId,
    string? Role,
    string Action,
    DateTimeOffset AttemptedAt) : INotification;
