using System.Net;
using System.Text.Json;
using SmartRail.Host.IntegrationTests.Infrastructure;
using SmartRail.SharedKernel.Application.Abstractions;

namespace SmartRail.Host.IntegrationTests.LiveOperations;

/// <summary>
/// The published delay range (status-publication spec): the read names the same limits the API
/// enforces, and only the Network Controller may read it.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PublishingRulesTests(PostgresFixture postgres)
{
    private const string Route = "/api/v1/operations/publishing-rules";

    [Fact]
    public async Task The_controller_reads_the_delay_range_and_other_callers_are_refused()
    {
        using var factory = new SmartRailApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();

        using var response = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, Route)
                .WithStaffToken(TestTokens.Create("controller.mahesh", Roles.NetworkController)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var delay = document.RootElement.GetProperty("delay");

        Assert.Equal(1, delay.GetProperty("minMinutes").GetInt32());
        Assert.Equal(720, delay.GetProperty("maxMinutes").GetInt32());

        using var guard = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, Route)
                .WithStaffToken(TestTokens.Create("guard.ishara", Roles.Guard)));
        Assert.Equal(HttpStatusCode.Forbidden, guard.StatusCode);

        using var anonymous = await client.GetAsync(Route);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }
}
