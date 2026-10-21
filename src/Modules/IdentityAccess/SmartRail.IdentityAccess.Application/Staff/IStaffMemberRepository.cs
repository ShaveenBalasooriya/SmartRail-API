namespace SmartRail.IdentityAccess.Application.Staff;

/// <summary>Reads the module's staff accounts from its own schema.</summary>
public interface IStaffMemberRepository
{
    /// <summary>
    /// The staff account whose identity-provider subject matches, or <see langword="null"/> when no
    /// account does.
    /// </summary>
    Task<StaffMemberResponse?> GetBySubjectAsync(
        string subject,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The display name of each listed staff account, keyed by staff ID; an ID with no account is
    /// absent.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(
        IReadOnlyCollection<Guid> staffIds,
        CancellationToken cancellationToken = default);
}
