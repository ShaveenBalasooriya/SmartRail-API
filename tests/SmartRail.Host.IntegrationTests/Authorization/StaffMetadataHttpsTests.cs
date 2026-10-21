using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmartRail.Host.IntegrationTests.Infrastructure;
using SmartRail.IdentityAccess.Api.Authentication;

namespace SmartRail.Host.IntegrationTests.Authorization;

/// <summary>
/// The staff handler accepts a plain-http discovery address only where the local stack runs
/// (Development and E2E). Every other environment must reach the issuer over https (design D10).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class StaffMetadataHttpsTests(PostgresFixture postgres)
{
    [Theory]
    [InlineData("Development", false)]
    [InlineData("E2E", false)]
    [InlineData("Staging", true)]
    [InlineData("Production", true)]
    public void A_plain_http_metadata_address_requires_https_outside_development_and_e2e(
        string environment,
        bool requireHttps)
    {
        using var factory = new SmartRailApiFactory(postgres.ConnectionString, environment: environment);

        var options = factory.Services
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(StaffAuthentication.Scheme);

        Assert.Equal(requireHttps, options.RequireHttpsMetadata);
    }
}
