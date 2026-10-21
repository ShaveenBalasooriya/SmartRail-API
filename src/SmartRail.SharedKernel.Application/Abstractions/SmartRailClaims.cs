namespace SmartRail.SharedKernel.Application.Abstractions;

/// <summary>
/// The claims SmartRail adds to, or reads from, an authenticated principal (design D2). They are
/// private to the API: a client never sends <c>staff_id</c> or <c>smartrail_role</c>.
/// </summary>
public static class SmartRailClaims
{
    /// <summary>The resolved SmartRail staff account.</summary>
    public const string StaffId = "staff_id";

    /// <summary>The single catalogue role resolved from the token's <c>groups</c> claim.</summary>
    public const string Role = "smartrail_role";

    /// <summary>The provider identified by a valid API key; added by the provider scheme.</summary>
    public const string ProviderId = "provider_id";
}
