using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace SmartRail.Host.IntegrationTests.Infrastructure;

/// <summary>
/// Mints staff tokens locally, so the integration tests never need Authentik (design D11). The
/// factory swaps the staff scheme's signing key and issuer for these, and a token is signed exactly
/// as Authentik would sign one: same issuer, audience, <c>sub</c> and <c>groups</c>.
/// </summary>
internal static class TestTokens
{
    /// <summary>The issuer the locally signed tokens carry.</summary>
    public const string Issuer = "https://identity.test/application/o/smartrail-staff/";

    /// <summary>The staff client ID the tokens are issued for.</summary>
    public const string Audience = "smartrail-staff";

    /// <summary>The discovery address the Host is configured with; it is never fetched in a test.</summary>
    public const string MetadataAddress =
        "http://identity.test:9000/application/o/smartrail-staff/.well-known/openid-configuration";

    /// <summary>The claim the whole staff path reads the username from.</summary>
    public const string SubjectClaim = "sub";

    /// <summary>The Authentik authorization endpoint the document publishes for staff.</summary>
    public const string AuthorizationUrl = "https://identity.test/application/o/authorize/";

    /// <summary>The Authentik token endpoint the document publishes for staff.</summary>
    public const string TokenUrl = "https://identity.test/application/o/token/";

    private const string SigningKeyValue = "smartrail-integration-test-signing-key-of-sufficient-length";

    private static readonly SymmetricSecurityKey SigningKey =
        new(Encoding.UTF8.GetBytes(SigningKeyValue));

    /// <summary>The key the factory installs on the staff scheme.</summary>
    public static SymmetricSecurityKey SigningKeyForTests => SigningKey;

    /// <summary>A token for <paramref name="subject"/> in the named groups, valid for five minutes.</summary>
    public static string Create(string subject, params string[] groups) =>
        Create(subject, DateTimeOffset.UtcNow.AddMinutes(5), groups);

    /// <summary>A token with an explicit expiry, for the lifetime scenarios.</summary>
    public static string Create(string subject, DateTimeOffset expires, params string[] groups)
    {
        var claims = new ClaimsIdentity();
        claims.AddClaim(new Claim(SubjectClaim, subject));

        foreach (var group in groups)
        {
            claims.AddClaim(new Claim("groups", group));
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Subject = claims,
            Issuer = Issuer,
            Audience = Audience,
            IssuedAt = DateTime.UtcNow.AddMinutes(-1),
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
        });
    }

    /// <summary>Adds this token as a bearer credential to a request.</summary>
    public static HttpRequestMessage WithStaffToken(this HttpRequestMessage request, string token)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return request;
    }
}
