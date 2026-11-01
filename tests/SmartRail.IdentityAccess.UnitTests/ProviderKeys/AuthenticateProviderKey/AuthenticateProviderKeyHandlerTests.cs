using SmartRail.IdentityAccess.Application.ProviderKeys.AuthenticateProviderKey;
using SmartRail.IdentityAccess.UnitTests.TestDoubles;
using SmartRail.SharedKernel.Results;

namespace SmartRail.IdentityAccess.UnitTests.ProviderKeys.AuthenticateProviderKey;

public sealed class AuthenticateProviderKeyHandlerTests
{
    [Fact]
    public async Task Happy_path_authenticates_a_valid_key_for_an_active_provider()
    {
        // Arrange
        var key = TestData.Key();
        var keys = new FakeProviderApiKeyRepository(key);
        var providers = new StubCateringProviderDirectory(isActive: true);
        var handler = HandlerDriver.CreateQueryHandler<AuthenticateProviderKeyQuery, Guid>(keys, providers);

        // Act
        var result = await handler.Handle(
            new AuthenticateProviderKeyQuery(TestData.PlaintextKey),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(key.ProviderId, result.Value);
        Assert.Equal(key.KeyPrefix, Assert.Single(keys.RequestedPrefixes));
        Assert.Equal(key.ProviderId, Assert.Single(providers.RequestedProviderIds));
    }

    [Fact]
    public async Task A_malformed_key_is_unauthorized_without_a_repository_lookup()
    {
        // Arrange
        var keys = new FakeProviderApiKeyRepository();
        var providers = new StubCateringProviderDirectory();
        var handler = HandlerDriver.CreateQueryHandler<AuthenticateProviderKeyQuery, Guid>(keys, providers);

        // Act
        var result = await handler.Handle(new AuthenticateProviderKeyQuery("invalid"), CancellationToken.None);

        // Assert
        AssertUnauthorized(result);
        Assert.Empty(keys.RequestedPrefixes);
        Assert.Empty(providers.RequestedProviderIds);
    }

    [Fact]
    public async Task An_unknown_prefix_is_unauthorized_without_checking_the_provider()
    {
        // Arrange
        var keys = new FakeProviderApiKeyRepository();
        var providers = new StubCateringProviderDirectory();
        var handler = HandlerDriver.CreateQueryHandler<AuthenticateProviderKeyQuery, Guid>(keys, providers);

        // Act
        var result = await handler.Handle(
            new AuthenticateProviderKeyQuery(TestData.PlaintextKey),
            CancellationToken.None);

        // Assert
        AssertUnauthorized(result);
        Assert.Equal("abcd1234", Assert.Single(keys.RequestedPrefixes));
        Assert.Empty(providers.RequestedProviderIds);
    }

    [Fact]
    public async Task A_wrong_secret_is_unauthorized_without_checking_the_provider()
    {
        // Arrange
        var keys = new FakeProviderApiKeyRepository(TestData.Key());
        var providers = new StubCateringProviderDirectory();
        var handler = HandlerDriver.CreateQueryHandler<AuthenticateProviderKeyQuery, Guid>(keys, providers);

        // Act
        var result = await handler.Handle(
            new AuthenticateProviderKeyQuery("srk_abcd1234_wrong-secret"),
            CancellationToken.None);

        // Assert
        AssertUnauthorized(result);
        Assert.Empty(providers.RequestedProviderIds);
    }

    [Fact]
    public async Task A_revoked_key_is_unauthorized_without_checking_the_provider()
    {
        // Arrange
        var keys = new FakeProviderApiKeyRepository(TestData.RevokedKey());
        var providers = new StubCateringProviderDirectory();
        var handler = HandlerDriver.CreateQueryHandler<AuthenticateProviderKeyQuery, Guid>(keys, providers);

        // Act
        var result = await handler.Handle(
            new AuthenticateProviderKeyQuery(TestData.PlaintextKey),
            CancellationToken.None);

        // Assert
        AssertUnauthorized(result);
        Assert.Empty(providers.RequestedProviderIds);
    }

    [Fact]
    public async Task A_valid_key_for_an_inactive_provider_is_unauthorized()
    {
        // Arrange
        var keys = new FakeProviderApiKeyRepository(TestData.Key());
        var providers = new StubCateringProviderDirectory(isActive: false);
        var handler = HandlerDriver.CreateQueryHandler<AuthenticateProviderKeyQuery, Guid>(keys, providers);

        // Act
        var result = await handler.Handle(
            new AuthenticateProviderKeyQuery(TestData.PlaintextKey),
            CancellationToken.None);

        // Assert
        AssertUnauthorized(result);
        Assert.Single(providers.RequestedProviderIds);
    }

    [Fact]
    public async Task A_key_store_failure_is_propagated_before_the_provider_is_checked()
    {
        // Arrange
        var keys = new FakeProviderApiKeyRepository
        {
            ExceptionToThrow = new InvalidOperationException("Key store unavailable."),
        };
        var providers = new StubCateringProviderDirectory();
        var handler = HandlerDriver.CreateQueryHandler<AuthenticateProviderKeyQuery, Guid>(keys, providers);

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new AuthenticateProviderKeyQuery(TestData.PlaintextKey),
            CancellationToken.None));

        // Assert
        Assert.Equal("Key store unavailable.", exception.Message);
        Assert.Empty(providers.RequestedProviderIds);
    }

    private static void AssertUnauthorized(Result<Guid> result)
    {
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Unauthorized, result.Error.Type);
        Assert.Equal("provider_key.invalid", result.Error.Code);
    }
}
