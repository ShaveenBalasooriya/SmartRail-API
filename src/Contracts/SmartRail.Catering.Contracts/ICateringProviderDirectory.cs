namespace SmartRail.Catering.Contracts;

/// <summary>
/// The one question another module may ask Catering about its providers: whether the provider a
/// catering API key belongs to is active. Identity authenticates the key against its own hash and
/// then asks this, because Catering owns the provider record (design D1; provider-data-api spec,
/// "Only active providers are served").
/// </summary>
public interface ICateringProviderDirectory
{
    /// <summary>
    /// True when Catering knows a provider with this identifier and it is active. An unknown provider
    /// is not active.
    /// </summary>
    Task<bool> IsActiveAsync(Guid providerId, CancellationToken cancellationToken = default);
}
