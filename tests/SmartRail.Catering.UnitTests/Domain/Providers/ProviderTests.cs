using SmartRail.Catering.Domain.Providers;
using ProviderEntity = SmartRail.Catering.Domain.Providers.Provider;
using SmartRail.Catering.UnitTests.TestDoubles;

namespace SmartRail.Catering.UnitTests.Domain.Providers;

public sealed class ProviderTests
{
    [Fact]
    public void Happy_path_creates_a_provider_with_its_seed_identity_and_trimmed_name()
    {
        // Arrange
        var providerId = Guid.CreateVersion7();

        // Act
        var result = ProviderEntity.Create(providerId, "  RailBites  ", true, TestData.Now);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(providerId, result.Value.Id);
        Assert.Equal("RailBites", result.Value.Name);
        Assert.True(result.Value.IsActive);
        Assert.Equal(TestData.Now, result.Value.CreatedAt);
    }

    [Fact]
    public void An_empty_seed_identifier_is_rejected()
    {
        // Arrange

        // Act
        var result = ProviderEntity.Create(Guid.Empty, "RailBites", true, TestData.Now);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ProviderErrors.IdRequired, result.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_provider_name_is_rejected(string? name)
    {
        // Arrange
        var providerId = Guid.CreateVersion7();

        // Act
        var result = ProviderEntity.Create(providerId, name!, true, TestData.Now);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ProviderErrors.NameRequired, result.Error);
    }
}
