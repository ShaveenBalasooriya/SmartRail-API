using SmartRail.SharedKernel.Application.Messaging;
using SmartRail.SharedKernel.Results;

namespace SmartRail.IdentityAccess.Application.Staff.GetStaffDisplayNames;

internal sealed class GetStaffDisplayNamesQueryHandler(IStaffMemberRepository staff)
    : IQueryHandler<GetStaffDisplayNamesQuery, IReadOnlyDictionary<Guid, string>>
{
    public async Task<Result<IReadOnlyDictionary<Guid, string>>> Handle(
        GetStaffDisplayNamesQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Result.Success(await staff.GetDisplayNamesAsync(request.StaffIds, cancellationToken));
    }
}
