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
    [Fact]
    public void A_plain_http_metadata_address_is_accepted_in_development()
    {
        using var factory = new SmartRailApiFactory(postgres.ConnectionString, environment: "Development");

        var options = factory.Services
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(StaffAuthentication.Scheme);

        Assert.False(options.RequireHttpsMetadata);
    }

    [Fact]
    public void A_plain_http_metadata_address_is_refused_in_production()
    {
        using var factory = new SmartRailApiFactory(
            postgres.ConnectionString,
            environment: "Production",
            settings: new Dictionary<string, string>
            {
                // Override the factory's https default with the local plain-http address.
                [StaffAuthentication.MetadataAddressSetting] = TestTokens.MetadataAddress,
            });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.Services
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(StaffAuthentication.Scheme));

        Assert.Contains("HTTPS", exception.Message, StringComparison.Ordinal);
    }
}
