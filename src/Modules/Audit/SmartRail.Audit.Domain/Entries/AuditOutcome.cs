namespace SmartRail.Audit.Domain.Entries;

/// <summary>What became of the staff action the entry records.</summary>
public enum AuditOutcome
{
    /// <summary>The action was permitted and committed.</summary>
    Succeeded = 0,

    /// <summary>The action was refused because the staff member's role did not allow it.</summary>
    Denied = 1,
}
