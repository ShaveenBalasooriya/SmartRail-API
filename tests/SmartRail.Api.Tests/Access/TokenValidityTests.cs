using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SmartRail.Api.Tests.Infrastructure;
using SmartRail.Host.IntegrationTests.Infrastructure;

namespace SmartRail.Api.Tests.Access;

/// <summary>
/// EP-02 "Done when": a token that is no longer valid, or that was not signed by the identity
/// provider, is refused with 401 and the standard error format. Both tokens are otherwise perfect:
/// the controller's subject, the controller's group, the right issuer and audience.
/// </summary>
[Collection(ApiDatabase.Name)]
public sealed class TokenValidityTests(ApiDatabaseFixture database) : ApiTest(database)
{
    [InProcessApiFact]
    public async Task An_expired_controller_token_is_refused_and_writes_nothing()
    {
        // Arrange
        var route = WriteRoute.PublishDelay;
        var ids = await Api.IdsAsync();
        var before = await Api.SnapshotAsync(route, ids);
        var expired = Credential.Staff(TestTokens.Create(
            "controller.mahesh",
            DateTimeOffset.UtcNow.AddHours(-1),
            "network_controller"));

        // Act
        using var response = await Api.SendAsync(route, ids, expired);

        // Assert
        await response.IsProblemAsync(HttpStatusCode.Unauthorized);
        await WriteRoute.AssertUnchangedAsync(Api, route, ids, before);
    }

    [ApiFact]
    public async Task A_controller_token_signed_with_an_untrusted_key_is_refused_and_writes_nothing()
    {
        // Arrange
        var route = WriteRoute.PublishDelay;
        var ids = await Api.IdsAsync();
        var before = await Api.SnapshotAsync(route, ids);
        var forged = Credential.Staff(TokenSignedWithAnotherKey("controller.mahesh", "network_controller"));

        // Act
        using var response = await Api.SendAsync(route, ids, forged);

        // Assert
        await response.IsProblemAsync(HttpStatusCode.Unauthorized);
        await WriteRoute.AssertUnchangedAsync(Api, route, ids, before);
    }

    /// <summary>
    /// Claims to be a staff token (same issuer, audience, subject, group and lifetime) but is signed
    /// with a key the Host does not hold, which is what a forged token looks like.
    /// </summary>
    private static string TokenSignedWithAnotherKey(string subject, string group)
    {
        var claims = new ClaimsIdentity();
        claims.AddClaim(new Claim(TestTokens.SubjectClaim, subject));
        claims.AddClaim(new Claim("groups", group));

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Subject = claims,
            Issuer = TestTokens.Issuer,
            Audience = TestTokens.Audience,
            IssuedAt = DateTime.UtcNow.AddMinutes(-1),
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes("smartrail-api-test-foreign-key-of-sufficient-length")),
                SecurityAlgorithms.HmacSha256),
        });
    }
}
