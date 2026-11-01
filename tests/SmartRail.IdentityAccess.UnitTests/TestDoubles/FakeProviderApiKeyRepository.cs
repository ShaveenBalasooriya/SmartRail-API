using SmartRail.IdentityAccess.Application.ProviderKeys;
using SmartRail.IdentityAccess.Domain.Entities;

namespace SmartRail.IdentityAccess.UnitTests.TestDoubles;

/// <summary>A key repository containing at most the key supplied by the test.</summary>
internal sealed class FakeProviderApiKeyRepository(ProviderApiKey? key = null) : IProviderApiKeyRepository
{
    public List<string> RequestedPrefixes { get; } = [];

    public Exception? ExceptionToThrow { get; set; }

    public Task<ProviderApiKey?> GetByPrefixAsync(
        string keyPrefix,
        CancellationToken cancellationToken = default)
    {
        RequestedPrefixes.Add(keyPrefix);
        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        return Task.FromResult(key?.KeyPrefix == keyPrefix ? key : null);
    }
}
