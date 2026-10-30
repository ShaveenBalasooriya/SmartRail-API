using System.Security.Cryptography;
using System.Text;

namespace SmartRail.Audit.Domain.Entries;

/// <summary>
/// The SHA-256 hash that chains audit entries (design D3). The hash covers the canonical JSON of
/// the entry's content followed by its previous hash, so changing any covered value or the link to
/// the entry before it breaks the chain from that entry onward.
/// </summary>
public static class AuditEntryHash
{
    /// <summary>The length of a hash in characters: 64 lowercase hexadecimal digits.</summary>
    public const int HexLength = 64;

    /// <summary>The previous hash of the first entry in the chain.</summary>
    public const string GenesisPreviousHash =
        "0000000000000000000000000000000000000000000000000000000000000000";

    /// <summary>Computes the hash of <paramref name="entry"/> from its content and previous hash.</summary>
    public static string Compute(AuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return Compute(AuditEntryCanonicalizer.Canonicalize(entry), entry.PreviousHash);
    }

    /// <summary>
    /// Hashes the canonical JSON of an entry's content immediately followed by its previous hash's
    /// 64-character hexadecimal text (design D3).
    /// </summary>
    public static string Compute(string canonicalJson, string previousHash)
    {
        ArgumentException.ThrowIfNullOrEmpty(canonicalJson);
        ArgumentException.ThrowIfNullOrEmpty(previousHash);

        var input = canonicalJson + previousHash;

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    }

    /// <summary>Whether <paramref name="value"/> is 64 hexadecimal digits.</summary>
    public static bool IsWellFormed(string? value) =>
        value is { Length: HexLength } && value.All(char.IsAsciiHexDigit);
}
