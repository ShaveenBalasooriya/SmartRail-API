using SmartRail.SharedKernel.Primitives;
using SmartRail.SharedKernel.Results;

namespace SmartRail.Catering.Domain.Providers;

/// <summary>
/// The external catering provider, RailBites in MVP 1. It is the module's root record: its
/// subscriptions and deliveries hang off it, and Identity asks whether it is active before serving a
/// provider key (design D1, <c>catering.providers</c>).
/// </summary>
/// <remarks>
/// The provider's plaintext API key is never stored. Identity keeps its prefix and SHA-256 hash in
/// <c>identity.provider_api_keys</c>, so a provider row carries no key material at all.
/// </remarks>
public sealed class Provider : Entity<Guid>
{
    private Provider()
    {
    }

    private Provider(Guid id, string name, bool isActive, DateTimeOffset createdAt)
        : base(id)
    {
        Name = name;
        IsActive = isActive;
        CreatedAt = createdAt;
    }

    /// <summary>The provider's display name.</summary>
    public string Name { get; private set; } = null!;

    /// <summary>False once the provider is deactivated; its keys then authenticate nothing.</summary>
    public bool IsActive { get; private set; }

    /// <summary>When the provider was seeded.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Creates a provider from its seed file, keeping the identifier the seed supplies so that the
    /// Identity key record and the provider record name the same provider (design D9).
    /// </summary>
    public static Result<Provider> Create(Guid id, string name, bool isActive, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            return Result.Failure<Provider>(ProviderErrors.IdRequired);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<Provider>(ProviderErrors.NameRequired);
        }

        return new Provider(id, name.Trim(), isActive, createdAt);
    }
}
