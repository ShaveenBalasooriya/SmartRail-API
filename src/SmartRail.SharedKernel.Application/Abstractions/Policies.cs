namespace SmartRail.SharedKernel.Application.Abstractions;

/// <summary>
/// The names of the authorization policies and rate-limit policies every module's endpoints use
/// (design D4, D8). The names are Shared Kernel constants so a module can classify an endpoint
/// without referencing Identity.
/// </summary>
public static class Policies
{
    /// <summary>The authorization policy every public endpoint declares.</summary>
    public const string Public = "public";

    /// <summary>The authorization policy every provider endpoint declares.</summary>
    public const string Provider = "provider";

    /// <summary>The authorization policy every staff endpoint declares.</summary>
    public const string Staff = "staff";

    /// <summary>The rate-limit policy applied to public endpoints.</summary>
    public const string PublicRateLimit = "public";

    /// <summary>The rate-limit policy applied to provider endpoints.</summary>
    public const string ProviderRateLimit = "provider";
}
