using SmartRail.SharedKernel.Results;

namespace SmartRail.Audit.Domain.Entries;

/// <summary>The expected failures of creating an <see cref="AuditEntry"/>.</summary>
public static class AuditEntryErrors
{
    public static readonly Error IdRequired =
        Error.Validation("audit_entry.id", "An audit entry identifier is required.");

    public static readonly Error ChainPositionRequired =
        Error.Validation("audit_entry.chain_position", "An audit entry's chain position must be one or greater.");

    public static readonly Error OccurredAtRequired =
        Error.Validation("audit_entry.occurred_at", "An audit entry needs the time the action occurred.");

    public static readonly Error ActorStaffIdRequired =
        Error.Validation("audit_entry.actor_staff_id", "A successful action needs the staff member who performed it.");

    public static readonly Error ActorRoleRequired =
        Error.Validation("audit_entry.actor_role", "A successful action needs the role the staff member acted in.");

    public static readonly Error ActorRoleBlank =
        Error.Validation("audit_entry.actor_role", "An audit entry's role cannot be blank.");

    public static readonly Error ActionRequired =
        Error.Validation("audit_entry.action", "An audit entry needs the action that was attempted.");

    public static readonly Error TargetTypeRequired =
        Error.Validation("audit_entry.target_type", "A successful action needs the type of the thing it changed.");

    public static readonly Error TargetTypeBlank =
        Error.Validation("audit_entry.target_type", "An audit entry's target type cannot be blank.");

    public static readonly Error TargetIdRequired =
        Error.Validation("audit_entry.target_id", "A successful action needs the identifier of the thing it changed.");

    public static readonly Error TargetIdBlank =
        Error.Validation("audit_entry.target_id", "An audit entry's target identifier cannot be blank.");

    public static readonly Error BeforeInvalidJson =
        Error.Validation("audit_entry.before", "An audit entry's before value must be valid JSON.");

    public static readonly Error AfterInvalidJson =
        Error.Validation("audit_entry.after", "An audit entry's after value must be valid JSON.");

    public static readonly Error PreviousHashInvalid =
        Error.Validation("audit_entry.previous_hash", "An audit entry's previous hash must be 64 hexadecimal digits.");
}
