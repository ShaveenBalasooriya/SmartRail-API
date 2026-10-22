using SmartRail.SharedKernel.Primitives;
using SmartRail.SharedKernel.Results;

namespace SmartRail.IdentityAccess.Domain.Entities;

/// <summary>
/// A staff member known to SmartRail. The identity provider owns the credential; this row only links
/// the provider's subject to a username and display name (data-model.md, <c>identity.staff_members</c>).
/// A role is not stored here: it is assigned as an identity-provider group and read from the token.
/// </summary>
public sealed class StaffMember : Entity<Guid>
{
    private StaffMember()
    {
    }

    private StaffMember(
        Guid id,
        string authentikSubject,
        string username,
        string displayName,
        DateTimeOffset createdAt)
        : base(id)
    {
        AuthentikSubject = authentikSubject;
        Username = username;
        DisplayName = displayName;
        CreatedAt = createdAt;
    }

    /// <summary>The identity provider's subject identifier, which is the username in subject mode.</summary>
    public string AuthentikSubject { get; private set; } = null!;

    /// <summary>The identity provider's username, which is the seed file's natural key.</summary>
    public string Username { get; private set; } = null!;

    /// <summary>The name shown to people.</summary>
    public string DisplayName { get; private set; } = null!;

    /// <summary>When the account was seeded.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Creates a staff member with the identifier supplied by its seed file, so that later data can
    /// reference a known staff ID.
    /// </summary>
    public static Result<StaffMember> Create(
        Guid id,
        string authentikSubject,
        string username,
        string displayName,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            return Result.Failure<StaffMember>(StaffMemberErrors.IdRequired);
        }

        if (string.IsNullOrWhiteSpace(authentikSubject))
        {
            return Result.Failure<StaffMember>(StaffMemberErrors.SubjectRequired);
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            return Result.Failure<StaffMember>(StaffMemberErrors.UsernameRequired);
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            return Result.Failure<StaffMember>(StaffMemberErrors.DisplayNameRequired);
        }

        return new StaffMember(
            id,
            authentikSubject.Trim(),
            username.Trim(),
            displayName.Trim(),
            createdAt);
    }
}
