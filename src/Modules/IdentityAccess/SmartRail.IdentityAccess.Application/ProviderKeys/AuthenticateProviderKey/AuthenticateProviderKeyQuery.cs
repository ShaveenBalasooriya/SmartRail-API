using SmartRail.SharedKernel.Application.Messaging;

namespace SmartRail.IdentityAccess.Application.ProviderKeys.AuthenticateProviderKey;

/// <summary>
/// Authenticates a provider's presented key and returns the provider it belongs to. Every failure —
/// a malformed key, an unknown prefix, a wrong secret or a revoked key — is the same unauthorized
/// result (design D7).
/// </summary>
public sealed record AuthenticateProviderKeyQuery(string PresentedKey) : IQuery<Guid>;
