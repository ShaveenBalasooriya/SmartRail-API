using System.Net;
using System.Text.Json;

namespace SmartRail.Api.Tests.Infrastructure;

/// <summary>
/// The two assertions every test shares, so a response is checked the same way everywhere and a
/// failure says what the API actually answered.
/// </summary>
internal static class ApiAssertions
{
    /// <summary>Asserts the status code, and includes the body in the failure so it explains itself.</summary>
    public static async Task<HttpResponseMessage> HasStatusAsync(
        this HttpResponseMessage response,
        HttpStatusCode expected)
    {
        if (response.StatusCode == expected)
        {
            return response;
        }

        var body = await response.Content.ReadAsStringAsync();

        Assert.Fail(
            $"Expected {(int)expected} ({expected}) but the API answered "
            + $"{(int)response.StatusCode} ({response.StatusCode}). Body: {body}");
        return response;
    }

    /// <summary>
    /// Asserts the error contract of every 4xx: the status, <c>application/problem+json</c>, the
    /// correlation ID the response header carries repeated in the body, and no stack trace or
    /// exception type name, which would leak internals (test-suite-review §5a phase C, item 4).
    /// </summary>
    public static async Task<string> IsProblemAsync(
        this HttpResponseMessage response,
        HttpStatusCode expected)
    {
        await response.HasStatusAsync(expected);

        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var document = JsonDocument.Parse(body);
        var problem = document.RootElement;

        Assert.Equal((int)expected, problem.GetProperty("status").GetInt32());

        var correlationId = Assert.Single(response.Headers.GetValues(ApiRequests.CorrelationHeader));
        Assert.Equal(correlationId, problem.GetProperty("traceId").GetString());

        Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);

        return body;
    }
}
