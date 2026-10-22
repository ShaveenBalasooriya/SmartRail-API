using MediatR;
using SmartRail.IdentityAccess.Application.Staff.GetStaffDisplayNames;
using SmartRail.IdentityAccess.Contracts;

namespace SmartRail.IdentityAccess.Application.Staff;

/// <summary>
/// Implements the module's staff directory contract by sending its own query, so another module's
/// call and an HTTP request run through the same handler (architecture §3). The contract cannot
/// fail, so the query's successful value is unwrapped.
/// </summary>
internal sealed class StaffDirectoryService(ISender sender) : IStaffDirectory
{
    public async Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(
        IReadOnlyCollection<Guid> staffIds,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(new GetStaffDisplayNamesQuery(staffIds), cancellationToken);

        return result.Value;
    }
}
