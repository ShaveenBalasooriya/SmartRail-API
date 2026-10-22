using SmartRail.SharedKernel.Application.Messaging;
using SmartRail.SharedKernel.Results;

namespace SmartRail.IdentityAccess.Application.Staff.GetStaffMemberBySubject;

internal sealed class GetStaffMemberBySubjectQueryHandler(IStaffMemberRepository staff)
    : IQueryHandler<GetStaffMemberBySubjectQuery, StaffMemberResponse>
{
    private static readonly Error StaffMemberNotFound =
        Error.NotFound("staff.not_found", "No staff account matches this subject.");

    public async Task<Result<StaffMemberResponse>> Handle(
        GetStaffMemberBySubjectQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var staffMember = await staff.GetBySubjectAsync(request.Subject, cancellationToken);

        return staffMember is null
            ? Result.Failure<StaffMemberResponse>(StaffMemberNotFound)
            : Result.Success(staffMember);
    }
}
