using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SmartRail.Host.Http;
using SmartRail.Host.IntegrationTests.Flows.Support;
using SmartRail.Host.IntegrationTests.Infrastructure;

namespace SmartRail.Host.IntegrationTests.Flows.Drivers;

/// <summary>One departure log as the Guard's sync returns it.</summary>
/// <param name="Id">The client-generated identifier the Guard's queue sent.</param>
/// <param name="JourneyId">The journey whose stop departed.</param>
/// <param name="StopSequence">The stop that departed.</param>
/// <param name="DepartedAt">The device time of the tap.</param>
/// <param name="ReceivedAt">The server time the log arrived.</param>
internal sealed record DepartureLog(
    Guid Id,
    Guid JourneyId,
    int StopSequence,
    DateTimeOffset DepartedAt,
    DateTimeOffset ReceivedAt);

/// <summary>
/// The Guard's surface: the offline queue's departure sync and the journeys the guard works against.
/// The driver is the surface and the session is the caller, so a Guard who is not assigned the
/// journey — or no credential at all — is the same typed call with a different (or no) token.
/// </summary>
internal sealed class GuardDriver
{
    private readonly HttpClient _client;
    private readonly StaffSession? _session;

    /// <param name="client">The flow's HTTP client.</param>
    /// <param name="session">The Guard acting, or null to send no credential at all.</param>
    public GuardDriver(HttpClient client, StaffSession? session)
    {
        _client = client;
        _session = session;
    }

    /// <summary>
    /// Logs a departure at one calling stop of a journey assigned to this guard. The identifier is the
    /// queue's own, so replaying a queued entry sends the same one again; when the caller does not
    /// name one, a fresh identifier is used, as a first tap would.
    /// </summary>
    public async Task<ApiAttempt<DepartureLog>> LogDepartureAsync(
        Guid journeyId,
        int stopSequence,
        DateTimeOffset departedAt,
        string? delayCause = "crew",
        Guid? id = null)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{ApiRoutes.BasePath}/guard/departure-logs")
        {
            Content = JsonContent.Create(new
            {
                id = id ?? Guid.CreateVersion7(),
                journeyId,
                stopSequence,
                departedAt = departedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                delayCause,
            }),
        };

        if (_session is not null)
        {
            request.WithStaffToken(_session.Token);
        }

        using var response = await _client.SendAsync(request);

        var text = await response.Content.ReadAsStringAsync();
        var mediaType = response.Content.Headers.ContentType?.MediaType;

        DepartureLog? value = null;

        if (response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK && text.Length > 0)
        {
            value = JsonSerializer.Deserialize<DepartureLog>(text, JsonSerializerOptions.Web);
        }

        return new ApiAttempt<DepartureLog>(response.StatusCode, mediaType, text, value);
    }

    /// <summary>Reads one journey as this guard's view shows it.</summary>
    public async Task<ApiAttempt<StaffJourneyView>> JourneyViewAsync(Guid journeyId)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{ApiRoutes.BasePath}/guard/journeys/{journeyId}");

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
            value = StaffViewJson.Detail(document.RootElement, stopsInsideJourney: true);
        }

        return new ApiAttempt<StaffJourneyView>(response.StatusCode, mediaType, text, value);
    }

    /// <summary>Opens the guard's live stream as this session, before a change is published.</summary>
    public Task<SseStream> OpenStreamAsync() => SseStream.OpenGuardAsync(_client, _session);
}
