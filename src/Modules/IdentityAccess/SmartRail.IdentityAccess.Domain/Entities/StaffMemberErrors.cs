using SmartRail.SharedKernel.Results;

namespace SmartRail.IdentityAccess.Domain.Entities;

/// <summary>The expected failures of creating a <see cref="StaffMember"/>.</summary>
public static class StaffMemberErrors
{
    public static readonly Error IdRequired =
        Error.Validation("staff_member.id", "A staff member identifier is required.");

    public static readonly Error SubjectRequired =
        Error.Validation("staff_member.subject", "An identity provider subject is required.");

    public static readonly Error UsernameRequired =
        Error.Validation("staff_member.username", "A username is required.");

    public static readonly Error DisplayNameRequired =
        Error.Validation("staff_member.display_name", "A display name is required.");
}
