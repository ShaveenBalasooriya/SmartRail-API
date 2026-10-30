using System.Net.Http.Json;
using System.Text.Json;
using SmartRail.SimulatedProvider.Configuration;
using SmartRail.SimulatedProvider.Processing;

namespace SmartRail.SimulatedProvider.Clients;

/// <summary>The registration the provider made, or the error that stopped it.</summary>
public sealed record WebhookRegistrationResult(Guid? SubscriptionId, string? Error);

/// <summary>
/// The only way the simulated provider talks to SmartRail: its provider API, with its API key
/// (simulated-provider spec, "Separate process"; design D6). No SmartRail type is referenced.
/// </summary>
public sealed class SmartRailProviderClient(HttpClient http)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Registers this provider's callback URL, replacing any earlier one.</summary>
    public async Task<WebhookRegistrationResult> RegisterWebhookAsync(
        string callbackUrl,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "api/v1/provider/webhook")
        {
            Content = JsonContent.Create(new { url = callbackUrl }),
        };

        using var response = await http.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return new WebhookRegistrationResult(
                null,
                $"registration answered {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        var subscription = await response.Content.ReadFromJsonAsync<WebhookSubscriptionItem>(
            JsonOptions,
            cancellationToken);

        return new WebhookRegistrationResult(subscription?.Id, null);
    }

    /// <summary>One station, or null when SmartRail does not know it.</summary>
    public async Task<ProviderStation?> GetStationAsync(
        Guid stationId,
        CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(
            $"api/v1/provider/stations/{stationId}",
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var station = await response.Content.ReadFromJsonAsync<StationItem>(JsonOptions, cancellationToken);

        return station is null ? null : new ProviderStation(station.Id, station.Name);
    }

    /// <summary>Today's journeys calling at the station, in the provider API's arrival order.</summary>
    public async Task<IReadOnlyList<ProviderJourney>> GetJourneysAsync(
        Guid stationId,
        CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(
            $"api/v1/provider/stations/{stationId}/journeys",
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var journeys = await response.Content.ReadFromJsonAsync<List<JourneyItem>>(
            JsonOptions,
            cancellationToken);

        return
        [
            .. (journeys ?? []).Select(journey => new ProviderJourney(
                journey.JourneyId,
                journey.Line?.Name,
                journey.Direction,
                journey.FinalDestination?.Id,
                journey.FinalDestination?.Name,
                journey.ScheduledArrival,
                journey.ExpectedArrival,
                journey.Platform?.Number,
                journey.Status ?? "scheduled",
                journey.SequenceNumber)),
        ];
    }

    private sealed record WebhookSubscriptionItem(Guid Id, string Url, bool IsActive, DateTimeOffset CreatedAt);

    private sealed record StationItem(Guid Id, string Name);

    private sealed record LineItem(Guid Id, string? Name);

    private sealed record StationRefItem(Guid Id, string? Name);

    private sealed record PlatformItem(Guid Id, string Number);

    private sealed record JourneyItem(
        Guid JourneyId,
        LineItem? Line,
        string? Direction,
        StationRefItem? FinalDestination,
        DateTimeOffset? ScheduledArrival,
        DateTimeOffset? ExpectedArrival,
        PlatformItem? Platform,
        string? Status,
        long SequenceNumber);
}
