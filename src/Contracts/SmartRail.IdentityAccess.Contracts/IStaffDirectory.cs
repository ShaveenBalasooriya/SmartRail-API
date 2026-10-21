namespace SmartRail.IdentityAccess.Contracts;

/// <summary>
/// Resolves staff IDs held by other modules to display names (architecture §3). Other modules use
/// this instead of reading the <c>identity</c> schema. Staff-facing endpoints only: public and
/// provider responses never carry staff names or IDs.
/// </summary>
public interface IStaffDirectory
{
    /// <summary>
    /// The display name of each requested staff ID that has an account; an unknown ID is simply
    /// absent from the result.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(
        IReadOnlyCollection<Guid> staffIds,
        CancellationToken cancellationToken = default);
}
