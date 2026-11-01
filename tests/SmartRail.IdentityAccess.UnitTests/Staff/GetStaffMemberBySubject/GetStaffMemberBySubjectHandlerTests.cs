using SmartRail.IdentityAccess.Application.Staff;
using SmartRail.IdentityAccess.Application.Staff.GetStaffMemberBySubject;
using SmartRail.IdentityAccess.UnitTests.TestDoubles;
using SmartRail.SharedKernel.Results;

namespace SmartRail.IdentityAccess.UnitTests.Staff.GetStaffMemberBySubject;

public sealed class GetStaffMemberBySubjectHandlerTests
{
    [Fact]
    public async Task Happy_path_returns_the_staff_account_matching_the_subject()
    {
        // Arrange
        var staffMember = new StaffMemberResponse(Guid.CreateVersion7(), "controller.one", "Controller One");
        var staff = new FakeStaffMemberRepository();
        staff.Add(staffMember, "controller.one");
        var handler = HandlerDriver.CreateQueryHandler<GetStaffMemberBySubjectQuery, StaffMemberResponse>(staff);

        // Act
        var result = await handler.Handle(
            new GetStaffMemberBySubjectQuery("controller.one"),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(staffMember, result.Value);
        Assert.Equal("controller.one", Assert.Single(staff.RequestedSubjects));
    }

    [Fact]
    public async Task An_unknown_subject_returns_not_found()
    {
        // Arrange
        var staff = new FakeStaffMemberRepository();
        var handler = HandlerDriver.CreateQueryHandler<GetStaffMemberBySubjectQuery, StaffMemberResponse>(staff);

        // Act
        var result = await handler.Handle(new GetStaffMemberBySubjectQuery("unknown"), CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    [Fact]
    public async Task A_staff_store_failure_is_propagated()
    {
        // Arrange
        var staff = new FakeStaffMemberRepository
        {
            ExceptionToThrow = new InvalidOperationException("Staff store unavailable."),
        };
        var handler = HandlerDriver.CreateQueryHandler<GetStaffMemberBySubjectQuery, StaffMemberResponse>(staff);

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new GetStaffMemberBySubjectQuery("controller.one"), CancellationToken.None));

        // Assert
        Assert.Equal("Staff store unavailable.", exception.Message);
    }
}
