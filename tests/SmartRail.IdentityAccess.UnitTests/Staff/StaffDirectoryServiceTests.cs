using SmartRail.IdentityAccess.Application.Staff.GetStaffDisplayNames;
using SmartRail.IdentityAccess.Contracts;
using SmartRail.IdentityAccess.UnitTests.TestDoubles;

namespace SmartRail.IdentityAccess.UnitTests.Staff;

public sealed class StaffDirectoryServiceTests
{
    [Fact]
    public async Task Happy_path_sends_the_modules_query_and_returns_its_display_names()
    {
        // Arrange
        var staffId = Guid.CreateVersion7();
        IReadOnlyDictionary<Guid, string> names = new Dictionary<Guid, string>
        {
            [staffId] = "Controller One",
        };
        var sender = new SpySender(names);
        var service = HandlerDriver.CreateService<IStaffDirectory>(
            typeof(GetStaffDisplayNamesQuery).Assembly,
            sender);
        var requestedIds = new[] { staffId };

        // Act
        var result = await service.GetDisplayNamesAsync(requestedIds, CancellationToken.None);

        // Assert
        Assert.Same(names, result);
        var query = Assert.Single(sender.Queries);
        Assert.Same(requestedIds, query.StaffIds);
    }
}
