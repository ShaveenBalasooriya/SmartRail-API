using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
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
/// The three seeded staff identities (<c>seed/identity/staff.json</c>) a flow acts as, plus the two
/// unusable variants the authentication cases need. The role comes from the token's group and the
/// staff id from its subject, exactly as the Host resolves them.
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

    /// <summary>The controller's identity with a token that expired an hour ago.</summary>
    public static StaffSession ControllerWithExpiredToken { get; } = new(
        "controller.mahesh",
        Roles.NetworkController,
        ControllerId,
        () => TestTokens.Create(
            "controller.mahesh",
            DateTimeOffset.UtcNow.AddHours(-1),
            Roles.NetworkController));

    /// <summary>The controller's identity with a token signed by a key the Host does not trust.</summary>
    public static StaffSession ControllerWithForeignKey { get; } = new(
        "controller.mahesh",
        Roles.NetworkController,
        ControllerId,
        () => ForeignToken.Create("controller.mahesh", Roles.NetworkController));
}

/// <summary>
/// Mints a token that claims to be a staff token — same issuer, audience, subject, role and lifetime —
/// but is signed with a key the Host's staff scheme does not hold. Authentication has to refuse it on
/// the signature, which is what a forged token looks like.
/// </summary>
internal static class ForeignToken
{
    private static readonly SymmetricSecurityKey SigningKey = new(
        Encoding.UTF8.GetBytes("smartrail-integration-test-foreign-key-of-sufficient-length"));

    /// <summary>A token for <paramref name="subject"/> in the named role, signed with the foreign key.</summary>
    public static string Create(string subject, string role)
    {
        var claims = new System.Security.Claims.ClaimsIdentity();
        claims.AddClaim(new System.Security.Claims.Claim(TestTokens.SubjectClaim, subject));
        claims.AddClaim(new System.Security.Claims.Claim("groups", role));

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Subject = claims,
            Issuer = TestTokens.Issuer,
            Audience = TestTokens.Audience,
            IssuedAt = DateTime.UtcNow.AddMinutes(-1),
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
        });
    }
}
