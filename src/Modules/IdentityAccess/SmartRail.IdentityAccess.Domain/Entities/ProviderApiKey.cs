using System.Security.Cryptography;
using System.Text;
using SmartRail.SharedKernel.Primitives;
using SmartRail.SharedKernel.Results;

namespace SmartRail.IdentityAccess.Domain.Entities;

/// <summary>
/// The catering provider's API key. Only a short lookup prefix and a SHA-256 hash of the full key are
/// stored, so the plaintext key never reaches the database (design D7, <c>identity.provider_api_keys</c>).
/// </summary>
public sealed class ProviderApiKey : Entity<Guid>
{
    /// <summary>The scheme every key starts with.</summary>
    public const string Scheme = "srk_";

    /// <summary>The number of characters between the scheme and the secret.</summary>
    public const int PrefixLength = 8;

    private ProviderApiKey()
    {
    }

    private ProviderApiKey(
        Guid id,
        Guid providerId,
        string keyPrefix,
        string keyHash,
        DateTimeOffset createdAt)
        : base(id)
    {
        ProviderId = providerId;
        KeyPrefix = keyPrefix;
        KeyHash = keyHash;
        CreatedAt = createdAt;
    }

    /// <summary>The catering provider this key belongs to.</summary>
    public Guid ProviderId { get; private set; }

    /// <summary>The non-secret lookup prefix, which is the key's natural key.</summary>
    public string KeyPrefix { get; private set; } = null!;

    /// <summary>The lowercase hexadecimal SHA-256 of the full key.</summary>
    public string KeyHash { get; private set; } = null!;

    /// <summary>When the key was issued.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When the key was revoked, or <see langword="null"/> while it is active.</summary>
    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>True once a revocation time is recorded; a revoked key authenticates nothing.</summary>
    public bool IsRevoked => RevokedAt is not null;

    /// <summary>
    /// Issues a key from its plaintext, keeping only the prefix and the hash. The identifier is
    /// generated with <see cref="Guid.CreateVersion7"/>.
    /// </summary>
    public static Result<ProviderApiKey> Issue(Guid providerId, string plaintextKey, DateTimeOffset now)
    {
        if (providerId == Guid.Empty)
        {
            return Result.Failure<ProviderApiKey>(ProviderApiKeyErrors.ProviderIdRequired);
        }

        var prefix = ExtractPrefix(plaintextKey);
        if (prefix is null)
        {
            return Result.Failure<ProviderApiKey>(ProviderApiKeyErrors.InvalidFormat);
        }

        return new ProviderApiKey(Guid.CreateVersion7(), providerId, prefix, Hash(plaintextKey), now);
    }

    /// <summary>
    /// True when the presented key has this key's prefix and the same hash. The hashes are compared
    /// in constant time, so a wrong key reveals nothing through its duration.
    /// </summary>
    public bool Matches(string presentedKey)
    {
        if (!string.Equals(ExtractPrefix(presentedKey), KeyPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var presentedHash = SHA256.HashData(Encoding.UTF8.GetBytes(presentedKey));
        var storedHash = Convert.FromHexString(KeyHash);

        return CryptographicOperations.FixedTimeEquals(presentedHash, storedHash);
    }

    /// <summary>
    /// Reads the lookup prefix out of a presented key, or <see langword="null"/> when the key is not
    /// in the <c>srk_&lt;8-character prefix&gt;_&lt;secret&gt;</c> form.
    /// </summary>
    public static string? ExtractPrefix(string? presentedKey)
    {
        if (presentedKey is null || !presentedKey.StartsWith(Scheme, StringComparison.Ordinal))
        {
            return null;
        }

        var separatorIndex = Scheme.Length + PrefixLength;
        if (presentedKey.Length <= separatorIndex + 1 || presentedKey[separatorIndex] != '_')
        {
            return null;
        }

        var prefix = presentedKey.AsSpan(Scheme.Length, PrefixLength);
        foreach (var character in prefix)
        {
            if (!char.IsAsciiLetterOrDigit(character))
            {
                return null;
            }
        }

        return new string(prefix);
    }

    private static string Hash(string plaintextKey) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(plaintextKey)));
}
