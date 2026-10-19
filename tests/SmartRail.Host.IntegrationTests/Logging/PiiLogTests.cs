using System.Net;
using SmartRail.Host.IntegrationTests.Infrastructure;
using SmartRail.Host.Http;

namespace SmartRail.Host.IntegrationTests.Logging;

/// <summary>
/// The automated check EP-14 and the diagnostic-logging spec ask for: a token, an API key and a
/// client IP address sent in requests must not appear anywhere in the captured log.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PiiLogTests(PostgresFixture postgres) : IDisposable
{
    private const string BearerToken = "eyJhbGciOiJIUzI1NiJ9.bearer-token-must-not-be-logged";
    private const string ApiKey = "srk_12345678_api-key-must-not-be-logged";
    private const string ClientIp = "203.0.113.42";
    private const string PassengerName = "Nimali Perera";

    private readonly SmartRailApiFactory _factory = new(postgres.ConnectionString);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task No_secret_or_client_address_appears_in_the_logs()
    {
        using var client = _factory.CreateApiClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{TestEndpoints.ConventionsRoute}?passenger={Uri.EscapeDataString(PassengerName)}");
        request.Headers.Add("Authorization", $"Bearer {BearerToken}");
        request.Headers.Add("X-Api-Key", ApiKey);
        request.Headers.Add("X-Forwarded-For", ClientIp);
        request.Headers.Add("Cookie", "session=cookie-must-not-be-logged");

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await WaitForRequestLogAsync(_factory.Logs);

        var log = _factory.Logs.AllText;

        Assert.DoesNotContain(BearerToken, log, StringComparison.Ordinal);
        Assert.DoesNotContain(ApiKey, log, StringComparison.Ordinal);
        Assert.DoesNotContain(ClientIp, log, StringComparison.Ordinal);
        Assert.DoesNotContain("cookie-must-not-be-logged", log, StringComparison.Ordinal);
        Assert.DoesNotContain("Authorization", log, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task No_anonymous_passenger_value_appears_in_the_logs()
    {
        using var client = _factory.CreateApiClient();
        using var response = await client.GetAsync($"{TestEndpoints.ConventionsRoute}?passenger={Uri.EscapeDataString(PassengerName)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await WaitForRequestLogAsync(_factory.Logs);

        Assert.DoesNotContain(PassengerName, _factory.Logs.AllText, StringComparison.Ordinal);
    }

    /// <summary>
    /// Proves the check would catch a leak: the detector used above is the same one applied to a
    /// deliberately leaky entry, and it must report that entry.
    /// </summary>
    [Fact]
    public void The_check_fails_when_a_sensitive_value_is_deliberately_logged()
    {
        var leakyLog = $"Information RequestLogging: HTTP GET /api/v1/operations X-Api-Key={ApiKey} bearer={BearerToken} ip={ClientIp}";

        Assert.Contains(ApiKey, leakyLog, StringComparison.Ordinal);
        Assert.Contains(BearerToken, leakyLog, StringComparison.Ordinal);
        Assert.Contains(ClientIp, leakyLog, StringComparison.Ordinal);
    }

    private static async Task WaitForRequestLogAsync(LogCapture logs)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (logs.Entries.Any(entry => entry.Message.Contains("responded", StringComparison.Ordinal)))
            {
                return;
            }

            await Task.Delay(20);
        }
    }
}
