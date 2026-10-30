using Microsoft.EntityFrameworkCore;
using SmartRail.Catering.Contracts;
using SmartRail.Catering.Infrastructure.Persistence;

namespace SmartRail.Catering.Infrastructure.Providers;

/// <summary>
/// Answers Identity's question about a provider key: is the provider it belongs to still active?
/// The record is Catering's, so the lookup reads <c>catering.providers</c> and an unknown identifier
/// is not active (provider-data-api spec, "Only active providers are served").
/// </summary>
internal sealed class CateringProviderDirectory(CateringDbContext database) : ICateringProviderDirectory
{
    public Task<bool> IsActiveAsync(Guid providerId, CancellationToken cancellationToken = default) =>
        database.Providers.AnyAsync(
            provider => provider.Id == providerId && provider.IsActive,
            cancellationToken);
}
