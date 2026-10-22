namespace SmartRail.SharedKernel.Application.Abstractions;

/// <summary>
/// The whole role catalogue of MVP 1 (role-based-access spec). A role is the Authentik group of the
/// same name, read from the token's <c>groups</c> claim; nothing else grants one, and SmartRail
/// stores no role (owner decision, 2026-10-20).
/// </summary>
public static class Roles
{
    /// <summary>Publishes delays, cancellations and platform changes.</summary>
    public const string NetworkController = "network_controller";

    /// <summary>Logs the actual departure at every stop of an assigned journey.</summary>
    public const string Guard = "guard";

    /// <summary>Reads the audit record and changes nothing.</summary>
    public const string Auditor = "auditor";

    /// <summary>Every role a staff token may resolve to.</summary>
    public static IReadOnlyList<string> All { get; } = [NetworkController, Guard, Auditor];
}
