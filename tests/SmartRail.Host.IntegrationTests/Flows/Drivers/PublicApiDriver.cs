using System.Net;
using System.Text.Json;
using SmartRail.Host.Http;
using SmartRail.Host.IntegrationTests.Flows.Support;

namespace SmartRail.Host.IntegrationTests.Flows.Drivers;

/// <summary>One journey as the anonymous passenger channel shows it.</summary>
/// <param name="RawJson">
/// The response exactly as it arrived. A flow that compares a payload before and after a refusal
/// keeps this, so what a passenger would see is byte-for-byte what the flow held on to.
/// </param>
internal sealed record PublicJourney(string RawJson);

/// <summary>
/// The public passenger channel: the anonymous journey detail, the platform board a station display
/// reads, and the live stream. A flow reads the same journey from each channel, so a change that is
/// meant to reach every channel can be checked on each of them.
/// </summary>
internal sealed class PublicApiDriver
{
    private readonly HttpClient _client;

    public PublicApiDriver(HttpClient client) => _client = client;

    /// <summary>Reads one journey's public detail, anonymously.</summary>
    public async Task<ApiAttempt<PublicJourney>> JourneyAsync(Guid journeyId)
    {
        using var response = await _client.GetAsync(JourneyRoute(journeyId));

        var text = await response.Content.ReadAsStringAsync();
        var mediaType = response.Content.Headers.ContentType?.MediaType;

        PublicJourney? value = response.StatusCode == HttpStatusCode.OK
            ? new PublicJourney(text)
            : null;

        return new ApiAttempt<PublicJourney>(response.StatusCode, mediaType, text, value);
    }

    /// <summary>Reads one journey's public detail as the values a flow asserts on.</summary>
    public async Task<ApiAttempt<PublicJourneyView>> JourneyViewAsync(Guid journeyId)
    {
        using var response = await _client.GetAsync(JourneyRoute(journeyId));

        var text = await response.Content.ReadAsStringAsync();
        var mediaType = response.Content.Headers.ContentType?.MediaType;

        PublicJourneyView? value = null;

        if (response.StatusCode == HttpStatusCode.OK)
        {
            using var document = JsonDocument.Parse(text);
            value = PublicViewJson.Journey(document.RootElement.GetProperty("data"));
        }

        return new ApiAttempt<PublicJourneyView>(response.StatusCode, mediaType, text, value);
    }

    /// <summary>Reads one platform's board, as a station display shows it.</summary>
    public async Task<ApiAttempt<PublicDisplayFeed>> DisplayAsync(Guid stationId, string platformNumber)
    {
        using var response = await _client.GetAsync(
            $"{ApiRoutes.BasePath}/public/display/{stationId}/platforms/{platformNumber}");

        var text = await response.Content.ReadAsStringAsync();
        var mediaType = response.Content.Headers.ContentType?.MediaType;

        PublicDisplayFeed? value = null;

        if (response.StatusCode == HttpStatusCode.OK)
        {
            using var document = JsonDocument.Parse(text);
            value = PublicViewJson.Feed(document.RootElement.GetProperty("data"));
        }

        return new ApiAttempt<PublicDisplayFeed>(response.StatusCode, mediaType, text, value);
    }

    /// <summary>Opens the live stream for one journey, before a change is published.</summary>
    public Task<SseStream> OpenStreamAsync(Guid journeyId) =>
        SseStream.OpenPublicAsync(_client, journeyId);

    /// <summary>Opens the live stream for one journey, resuming after <paramref name="lastEventId"/>.</summary>
    public Task<SseStream> ResumeStreamAsync(Guid journeyId, string lastEventId) =>
        SseStream.OpenPublicAsync(_client, journeyId, lastEventId);

    private static string JourneyRoute(Guid journeyId) =>
        $"{ApiRoutes.BasePath}/public/journeys/{journeyId}";
}
