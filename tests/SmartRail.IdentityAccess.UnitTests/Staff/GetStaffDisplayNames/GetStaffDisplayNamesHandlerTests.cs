using SmartRail.IdentityAccess.Application.Staff;
using SmartRail.IdentityAccess.Application.Staff.GetStaffDisplayNames;
using SmartRail.IdentityAccess.UnitTests.TestDoubles;

namespace SmartRail.IdentityAccess.UnitTests.Staff.GetStaffDisplayNames;

public sealed class GetStaffDisplayNamesHandlerTests
{
    [Fact]
    public async Task Happy_path_returns_known_names_and_leaves_unknown_ids_out()
    {
        // Arrange
        var known = new StaffMemberResponse(Guid.CreateVersion7(), "controller.one", "Controller One");
        var unknownId = Guid.CreateVersion7();
        var staff = new FakeStaffMemberRepository();
        staff.Add(known, "controller.one");
        var requestedIds = new[] { known.StaffId, unknownId };
        var handler = HandlerDriver.CreateQueryHandler<
            GetStaffDisplayNamesQuery,
            IReadOnlyDictionary<Guid, string>>(staff);

        // Act
        var result = await handler.Handle(new GetStaffDisplayNamesQuery(requestedIds), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(known.DisplayName, Assert.Single(result.Value).Value);
        Assert.Equal(known.StaffId, Assert.Single(result.Value).Key);
        Assert.Same(requestedIds, Assert.Single(staff.RequestedIdSets));
    }
}
