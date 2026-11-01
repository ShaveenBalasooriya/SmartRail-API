using SmartRail.Catering.Contracts;

namespace SmartRail.IdentityAccess.UnitTests.TestDoubles;

/// <summary>A provider directory with a configurable active answer.</summary>
internal sealed class StubCateringProviderDirectory(bool isActive = true) : ICateringProviderDirectory
{
    public List<Guid> RequestedProviderIds { get; } = [];

    public Task<bool> IsActiveAsync(Guid providerId, CancellationToken cancellationToken = default)
    {
        RequestedProviderIds.Add(providerId);
        return Task.FromResult(isActive);
    }
}
