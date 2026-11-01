using SmartRail.IdentityAccess.Application.Staff.GetStaffDisplayNames;
using SmartRail.IdentityAccess.Contracts;
using SmartRail.IdentityAccess.UnitTests.TestDoubles;

namespace SmartRail.IdentityAccess.UnitTests.AccessDenials;

public sealed class AccessDenialAnnouncerTests
{
    [Fact]
    public async Task Happy_path_publishes_one_denial_with_matching_event_identity_and_saves_it()
    {
        // Arrange
        var staffId = Guid.CreateVersion7();
        var publisher = new SpyIntegrationEventPublisher();
        var unitOfWork = new SpyUnitOfWork();
        var announcer = NewAnnouncer(
            publisher,
            unitOfWork,
            new StubCurrentUser(staffId, "guard"));

        // Act
        await announcer.AnnounceAsync("LogDeparture", CancellationToken.None);

        // Assert
        var publication = Assert.Single(publisher.Publications);
        var denial = Assert.IsType<AccessDenied>(publication.IntegrationEvent);
        Assert.Equal(denial.EventId, publication.EventId);
        Assert.Null(publication.OrderingKey);
        Assert.Equal(staffId, denial.StaffId);
        Assert.Equal("guard", denial.Role);
        Assert.Equal("LogDeparture", denial.Action);
        Assert.Equal(TestData.Now, denial.AttemptedAt);
        Assert.Equal(1, unitOfWork.SaveChangesCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_missing_action_is_rejected_without_publishing_or_saving(string? action)
    {
        // Arrange
        var publisher = new SpyIntegrationEventPublisher();
        var unitOfWork = new SpyUnitOfWork();
        var announcer = NewAnnouncer(publisher, unitOfWork, new StubCurrentUser(null, null));

        // Act
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            announcer.AnnounceAsync(action!, CancellationToken.None));

        // Assert
        Assert.Empty(publisher.Publications);
        Assert.Equal(0, unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task A_save_failure_is_propagated_after_the_outbox_publication_was_requested()
    {
        // Arrange
        var publisher = new SpyIntegrationEventPublisher();
        var unitOfWork = new SpyUnitOfWork
        {
            ExceptionToThrow = new InvalidOperationException("Store unavailable."),
        };
        var announcer = NewAnnouncer(publisher, unitOfWork, new StubCurrentUser(null, null));

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            announcer.AnnounceAsync("PublishDelay", CancellationToken.None));

        // Assert
        Assert.Equal("Store unavailable.", exception.Message);
        Assert.Single(publisher.Publications);
        Assert.Equal(1, unitOfWork.SaveChangesCount);
    }

    private static IAccessDenials NewAnnouncer(
        SpyIntegrationEventPublisher publisher,
        SpyUnitOfWork unitOfWork,
        StubCurrentUser currentUser) =>
        HandlerDriver.CreateService<IAccessDenials>(
            typeof(GetStaffDisplayNamesQuery).Assembly,
            publisher,
            unitOfWork,
            currentUser,
            new StubTimeProvider(TestData.Now));
}
