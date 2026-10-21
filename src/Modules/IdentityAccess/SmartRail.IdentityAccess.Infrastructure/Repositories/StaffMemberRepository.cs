using Microsoft.EntityFrameworkCore;
using SmartRail.IdentityAccess.Application.Staff;
using SmartRail.IdentityAccess.Infrastructure.Persistence;

namespace SmartRail.IdentityAccess.Infrastructure.Repositories;

/// <summary>
/// Reads staff accounts from the module's own schema. The projection keeps the entity inside the
/// module; only the response crosses the boundary.
/// </summary>
internal sealed class StaffMemberRepository(IdentityDbContext database) : IStaffMemberRepository
{
    public async Task<StaffMemberResponse?> GetBySubjectAsync(
        string subject,
        CancellationToken cancellationToken = default)
    {
        return await database.StaffMembers
            .AsNoTracking()
            .Where(staffMember => staffMember.AuthentikSubject == subject)
            .Select(staffMember => new StaffMemberResponse(
                staffMember.Id,
                staffMember.Username,
                staffMember.DisplayName))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(
        IReadOnlyCollection<Guid> staffIds,
        CancellationToken cancellationToken = default)
    {
        return await database.StaffMembers
            .AsNoTracking()
            .Where(staffMember => staffIds.Contains(staffMember.Id))
            .ToDictionaryAsync(staffMember => staffMember.Id, staffMember => staffMember.DisplayName, cancellationToken);
    }
}
