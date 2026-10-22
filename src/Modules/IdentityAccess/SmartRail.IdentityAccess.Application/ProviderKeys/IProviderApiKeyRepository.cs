using SmartRail.IdentityAccess.Domain.Entities;

namespace SmartRail.IdentityAccess.Application.ProviderKeys;

/// <summary>Reads the module's provider API keys from its own schema.</summary>
public interface IProviderApiKeyRepository
{
    /// <summary>
    /// The key with this lookup prefix, or <see langword="null"/> when no key has it.
    /// </summary>
    Task<ProviderApiKey?> GetByPrefixAsync(
        string keyPrefix,
        CancellationToken cancellationToken = default);
}
