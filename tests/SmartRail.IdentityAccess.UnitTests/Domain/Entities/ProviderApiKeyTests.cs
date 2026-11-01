using SmartRail.IdentityAccess.Domain.Entities;
using SmartRail.IdentityAccess.UnitTests.TestDoubles;

namespace SmartRail.IdentityAccess.UnitTests.Domain.Entities;

public sealed class ProviderApiKeyTests
{
    [Fact]
    public void Happy_path_issues_only_a_lookup_prefix_and_hash_and_matches_the_original_key()
    {
        // Arrange
        var providerId = Guid.CreateVersion7();

        // Act
        var result = ProviderApiKey.Issue(providerId, TestData.PlaintextKey, TestData.Now);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(providerId, result.Value.ProviderId);
        Assert.Equal("abcd1234", result.Value.KeyPrefix);
        Assert.Equal(64, result.Value.KeyHash.Length);
        Assert.Equal(result.Value.KeyHash, result.Value.KeyHash.ToLowerInvariant());
        Assert.DoesNotContain(TestData.PlaintextKey, result.Value.KeyHash, StringComparison.Ordinal);
        Assert.Equal(TestData.Now, result.Value.CreatedAt);
        Assert.Null(result.Value.RevokedAt);
        Assert.False(result.Value.IsRevoked);
        Assert.True(result.Value.Matches(TestData.PlaintextKey));
    }

    [Fact]
    public void A_key_with_the_same_prefix_but_a_different_secret_does_not_match()
    {
        // Arrange
        var key = TestData.Key();

        // Act
        var matches = key.Matches("srk_abcd1234_wrong-secret");

        // Assert
        Assert.False(matches);
    }

    [Fact]
    public void A_key_with_a_different_prefix_does_not_match()
    {
        // Arrange
        var key = TestData.Key();

        // Act
        var matches = key.Matches("srk_wxyz9876_super-secret");

        // Assert
        Assert.False(matches);
    }

    [Fact]
    public void Extract_prefix_accepts_the_shortest_non_empty_secret()
    {
        // Arrange
        const string plaintext = "srk_abcd1234_x";

        // Act
        var prefix = ProviderApiKey.ExtractPrefix(plaintext);

        // Assert
        Assert.Equal("abcd1234", prefix);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abcd1234_secret")]
    [InlineData("srk_short_secret")]
    [InlineData("srk_abcd1234")]
    [InlineData("srk_abcd1234_")]
    [InlineData("srk_abcd1234-secret")]
    [InlineData("srk_abcd!234_secret")]
    public void An_invalid_key_format_has_no_lookup_prefix(string? plaintext)
    {
        // Arrange

        // Act
        var prefix = ProviderApiKey.ExtractPrefix(plaintext);

        // Assert
        Assert.Null(prefix);
    }

    [Fact]
    public void An_empty_provider_identifier_cannot_be_issued_a_key()
    {
        // Arrange

        // Act
        var result = ProviderApiKey.Issue(Guid.Empty, TestData.PlaintextKey, TestData.Now);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ProviderApiKeyErrors.ProviderIdRequired, result.Error);
    }

    [Fact]
    public void An_invalid_plaintext_key_cannot_be_issued()
    {
        // Arrange

        // Act
        var result = ProviderApiKey.Issue(Guid.CreateVersion7(), "not-a-smart-rail-key", TestData.Now);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ProviderApiKeyErrors.InvalidFormat, result.Error);
    }
}
