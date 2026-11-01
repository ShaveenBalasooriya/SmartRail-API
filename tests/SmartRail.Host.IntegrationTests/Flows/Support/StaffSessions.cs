using SmartRail.Host.IntegrationTests.Infrastructure;
using SmartRail.SharedKernel.Application.Abstractions;

namespace SmartRail.Host.IntegrationTests.Flows.Support;

/// <summary>
/// One staff member as a flow presents them: who the token's subject is, the role its group grants,
/// the seeded account it resolves to, and how its bearer token is minted. The token is minted on every
/// read because a test token lives five minutes, and a suite can run longer than that.
/// </summary>
internal sealed class StaffSession
{
    private readonly Func<string> _token;

    public StaffSession(string subject, string role, Guid staffId, Func<string> token)
    {
        Subject = subject;
        Role = role;
        StaffId = staffId;
        _token = token;
    }

    /// <summary>The token's <c>sub</c>, which is the seeded username.</summary>
    public string Subject { get; }

    /// <summary>The role the token's <c>groups</c> claim grants.</summary>
    public string Role { get; }

    /// <summary>The seeded SmartRail account the subject resolves to.</summary>
    public Guid StaffId { get; }

    /// <summary>A freshly minted bearer token for this session.</summary>
    public string Token => _token();
}

/// <summary>
/// The three seeded staff identities (<c>seed/identity/staff.json</c>) a flow acts as. The role comes
/// from the token's group and the staff id from its subject, exactly as the Host resolves them.
/// </summary>
internal static class StaffDirectory
{
    /// <summary>The seeded controller account.</summary>
    public static Guid ControllerId { get; } = Guid.Parse("01a15874-e69c-70da-a689-76a7268ed330");

    /// <summary>The seeded guard account.</summary>
    public static Guid GuardId { get; } = Guid.Parse("01a15874-e69d-73b2-b24d-d85d22fdead2");

    /// <summary>The seeded auditor account.</summary>
    public static Guid AuditorId { get; } = Guid.Parse("01a15874-e6a0-7fd3-8c46-34bd36e8f16d");

    /// <summary>The Network Controller: the only role that may publish a change.</summary>
    public static StaffSession Controller { get; } = new(
        "controller.mahesh",
        Roles.NetworkController,
        ControllerId,
        () => TestTokens.Create("controller.mahesh", Roles.NetworkController));

    /// <summary>The Guard, who may log departures and nothing else on the operations surface.</summary>
    public static StaffSession Guard { get; } = new(
        "guard.ishara",
        Roles.Guard,
        GuardId,
        () => TestTokens.Create("guard.ishara", Roles.Guard));

    /// <summary>
    /// A second Guard: a different staff account holding the guard group, so a journey assigned to
    /// another guard is a real journey this guard is not assigned to (the seed holds one guard).
    /// </summary>
    public static StaffSession SecondGuard { get; } = new(
        "auditor.wasantha",
        Roles.Guard,
        AuditorId,
        () => TestTokens.Create("auditor.wasantha", Roles.Guard));

    /// <summary>The Auditor, who may read the record and change nothing.</summary>
    public static StaffSession Auditor { get; } = new(
        "auditor.wasantha",
        Roles.Auditor,
        AuditorId,
        () => TestTokens.Create("auditor.wasantha", Roles.Auditor));
}
