using Microsoft.EntityFrameworkCore;
using SmartRail.IdentityAccess.Application.ProviderKeys;
using SmartRail.IdentityAccess.Domain.Entities;
using SmartRail.IdentityAccess.Infrastructure.Persistence;

namespace SmartRail.IdentityAccess.Infrastructure.Repositories;

/// <summary>Reads provider API keys by their lookup prefix from the module's own schema.</summary>
internal sealed class ProviderApiKeyRepository(IdentityDbContext database) : IProviderApiKeyRepository
{
    public async Task<ProviderApiKey?> GetByPrefixAsync(
        string keyPrefix,
        CancellationToken cancellationToken = default)
    {
        return await database.ProviderApiKeys
            .AsNoTracking()
            .SingleOrDefaultAsync(key => key.KeyPrefix == keyPrefix, cancellationToken);
    }
}
