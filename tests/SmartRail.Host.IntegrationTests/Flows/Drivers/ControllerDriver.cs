using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SmartRail.Host.Http;
using SmartRail.Host.IntegrationTests.Flows.Support;
using SmartRail.Host.IntegrationTests.Infrastructure;

namespace SmartRail.Host.IntegrationTests.Flows.Drivers;

/// <summary>
/// One published change as the API returns it. The flow reads only what it needs — the change's
/// identifier, so a correction can name it — and keeps the rest of the shape typed rather than
/// reaching into JSON in a test.
/// </summary>
/// <param name="Id">The change's identifier.</param>
/// <param name="Type">The kind of change, for example <c>delay</c>.</param>
/// <param name="Values">The values the change set, keyed by field name.</param>
/// <param name="PublishedAt">When the change was published.</param>
/// <param name="Provenance">How the change reached the channels.</param>
/// <param name="Corrected">True once a correction has withdrawn it.</param>
internal sealed record ControllerChange(
    Guid Id,
    string Type,
    IReadOnlyDictionary<string, string?> Values,
    DateTimeOffset PublishedAt,
    string Provenance,
    bool Corrected);

/// <summary>
/// The Network Controller's write surface — the five routes that publish a change — attempted as a
/// given staff session. The driver is the surface and the session is the caller, so a Guard or an
/// Auditor "publishing a delay" is the same typed call with a different token, which is exactly what
/// the separation-of-duties flow has to prove. A <see langword="null"/> session sends no credential.
/// </summary>
internal sealed class ControllerDriver
{
    private readonly HttpClient _client;
    private readonly StaffSession? _session;

    public ControllerDriver(HttpClient client, StaffSession? session)
    {
        _client = client;
        _session = session;
    }

    /// <summary>Publishes a delay from a calling stop that has not departed.</summary>
    public Task<ApiAttempt<ControllerChange>> PublishDelayAsync(
        Guid journeyId,
        int fromStopSequence,
        int minutes) =>
        SendAsync(
            $"{ApiRoutes.BasePath}/operations/journeys/{journeyId}/delays",
            new { fromStopSequence, minutes });

    /// <summary>Cancels a journey that has no recorded departure.</summary>
    public Task<ApiAttempt<ControllerChange>> PublishCancellationAsync(Guid journeyId) =>
        SendAsync($"{ApiRoutes.BasePath}/operations/journeys/{journeyId}/cancellation", body: null);

    /// <summary>Moves a calling stop that has not departed to another platform of its station.</summary>
    public Task<ApiAttempt<ControllerChange>> PublishPlatformChangeAsync(
        Guid journeyId,
        int stopSequence,
        Guid platformId) =>
        SendAsync(
            $"{ApiRoutes.BasePath}/operations/journeys/{journeyId}/platform-changes",
            new { stopSequence, platformId });

    /// <summary>Ends a running journey early at the last calling stop it served.</summary>
    public Task<ApiAttempt<ControllerChange>> PublishTerminationAsync(Guid journeyId, int lastStopSequence) =>
        SendAsync(
            $"{ApiRoutes.BasePath}/operations/journeys/{journeyId}/termination",
            new { lastStopSequence });

    /// <summary>Withdraws a published change by naming the change it corrects.</summary>
    public Task<ApiAttempt<ControllerChange>> PublishCorrectionAsync(Guid changeId) =>
        SendAsync($"{ApiRoutes.BasePath}/operations/changes/{changeId}/corrections", body: null);

    /// <summary>Reads one journey as the controller's live view shows it.</summary>
    public async Task<ApiAttempt<StaffJourneyView>> JourneyViewAsync(Guid journeyId)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{ApiRoutes.BasePath}/operations/journeys/{journeyId}");

        if (_session is not null)
        {
            request.WithStaffToken(_session.Token);
        }

        using var response = await _client.SendAsync(request);

        var text = await response.Content.ReadAsStringAsync();
        var mediaType = response.Content.Headers.ContentType?.MediaType;

        StaffJourneyView? value = null;

        if (response.StatusCode == HttpStatusCode.OK)
        {
            using var document = JsonDocument.Parse(text);
            value = StaffViewJson.Detail(document.RootElement, stopsInsideJourney: false);
        }

        return new ApiAttempt<StaffJourneyView>(response.StatusCode, mediaType, text, value);
    }

    private async Task<ApiAttempt<ControllerChange>> SendAsync(string route, object? body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, route);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (_session is not null)
        {
            request.WithStaffToken(_session.Token);
        }

        using var response = await _client.SendAsync(request);

        return await ReadAsync(response);
    }

    private static async Task<ApiAttempt<ControllerChange>> ReadAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        var mediaType = response.Content.Headers.ContentType?.MediaType;

        ControllerChange? value = null;

        if (response.StatusCode == HttpStatusCode.Created && text.Length > 0)
        {
            value = JsonSerializer.Deserialize<ControllerChange>(text, JsonSerializerOptions.Web);
        }

        return new ApiAttempt<ControllerChange>(response.StatusCode, mediaType, text, value);
    }
}
