using SmartRail.IdentityAccess.Domain.Entities;
using SmartRail.IdentityAccess.UnitTests.TestDoubles;

namespace SmartRail.IdentityAccess.UnitTests.Domain.Entities;

public sealed class StaffMemberTests
{
    [Fact]
    public void Happy_path_creates_a_seeded_staff_member_with_trimmed_display_values()
    {
        // Arrange
        var staffId = Guid.CreateVersion7();

        // Act
        var result = StaffMember.Create(
            staffId,
            "  controller.one  ",
            "  controller.one  ",
            "  Controller One  ",
            TestData.Now);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(staffId, result.Value.Id);
        Assert.Equal("controller.one", result.Value.AuthentikSubject);
        Assert.Equal("controller.one", result.Value.Username);
        Assert.Equal("Controller One", result.Value.DisplayName);
        Assert.Equal(TestData.Now, result.Value.CreatedAt);
    }

    [Fact]
    public void An_empty_seed_identifier_is_rejected()
    {
        // Arrange

        // Act
        var result = Create(id: Guid.Empty);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(StaffMemberErrors.IdRequired, result.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_identity_provider_subject_is_rejected(string? subject)
    {
        // Arrange

        // Act
        var result = Create(subject: subject!);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(StaffMemberErrors.SubjectRequired, result.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_username_is_rejected(string? username)
    {
        // Arrange

        // Act
        var result = Create(username: username!);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(StaffMemberErrors.UsernameRequired, result.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_display_name_is_rejected(string? displayName)
    {
        // Arrange

        // Act
        var result = Create(displayName: displayName!);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(StaffMemberErrors.DisplayNameRequired, result.Error);
    }

    private static SmartRail.SharedKernel.Results.Result<StaffMember> Create(
        Guid? id = null,
        string subject = "controller.one",
        string username = "controller.one",
        string displayName = "Controller One") =>
        StaffMember.Create(
            id ?? Guid.CreateVersion7(),
            subject,
            username,
            displayName,
            TestData.Now);
}
