using System.Text.Json;
using SmartRail.Catering.Domain.Webhooks;
using SmartRail.LiveOperations.Contracts;
using SmartRail.RailNetwork.Contracts;

namespace SmartRail.Catering.Application.Webhooks;

/// <summary>
/// Builds the versioned provider payload from the journey event the bus announced, read through
/// <see cref="ILiveJourneys"/> (webhook-delivery spec, "Event payload"; design D1, D5). The event
/// carries the resulting live state, so the delay, the platform and the last served stop are read
/// from the snapshot rather than from the publishing module's tables.
/// </summary>
public sealed class WebhookPayloadFactory(INetworkCatalog network)
{
    /// <summary>The provenance label every controller change carries in MVP 1.</summary>
    public const string AutomaticallyPublished = "automatically_published";

    /// <summary>Builds the JSON body of one delivery.</summary>
    public async Task<string> BuildAsync(
        WebhookEventType eventType,
        JourneyEventView journeyEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(journeyEvent);

        var stop = eventType switch
        {
            // A platform change is the stop whose platform changed most recently, which is the one
            // the event recorded.
            WebhookEventType.PlatformChanged => journeyEvent.Snapshot.Stops
                .Where(candidate => candidate.PlatformChangedAt is not null)
                .OrderByDescending(candidate => candidate.PlatformChangedAt)
                .ThenBy(candidate => candidate.Sequence)
                .FirstOrDefault(),

            // An early termination names the last stop the train actually served (design D5).
            WebhookEventType.TerminationPublished => journeyEvent.Snapshot.Stops
                .Where(candidate => candidate.ActualDeparture is not null)
                .OrderByDescending(candidate => candidate.Sequence)
                .FirstOrDefault(),

            _ => null,
        };

        var payload = new ProviderWebhookPayload(
            WebhookEventTypes.PayloadVersion,
            journeyEvent.Id,
            journeyEvent.SequenceNumber,
            WebhookEventTypes.ToStorageValue(eventType),
            journeyEvent.JourneyId,
            stop?.Sequence,
            stop?.StationId,
            await ValuesAsync(eventType, journeyEvent, stop, cancellationToken),
            journeyEvent.OccurredAt,
            AutomaticallyPublished,
            journeyEvent.CorrectsEventId);

        return JsonSerializer.Serialize(payload, WebhookEventTypes.PayloadJsonOptions);
    }

    /// <summary>
    /// The new values the type carries. A delay carries its minutes and a platform change its new
    /// platform; cancellation, termination and correction carry none, because their type already
    /// says what happened and a correction names the event it withdraws (design D5).
    /// </summary>
    private async Task<IReadOnlyDictionary<string, object?>> ValuesAsync(
        WebhookEventType eventType,
        JourneyEventView journeyEvent,
        LiveStopView? stop,
        CancellationToken cancellationToken)
    {
        switch (eventType)
        {
            case WebhookEventType.DelayPublished:
                return new Dictionary<string, object?>
                {
                    ["delayMinutes"] = journeyEvent.Snapshot.DelayMinutes,
                };

            case WebhookEventType.PlatformChanged:
                var values = new Dictionary<string, object?>
                {
                    ["platformId"] = stop?.PlatformId,
                };

                if (stop?.PlatformId is { } platformId)
                {
                    values["platformNumber"] = await PlatformNumberAsync(
                        stop.StationId,
                        platformId,
                        cancellationToken);
                }

                return values;

            default:
                return new Dictionary<string, object?>();
        }
    }

    /// <summary>The platform's number at its station, or null when the catalogue no longer holds it.</summary>
    private async Task<string?> PlatformNumberAsync(
        Guid stationId,
        Guid platformId,
        CancellationToken cancellationToken)
    {
        var platforms = await network.GetPlatformsAsync(stationId, cancellationToken);

        return platforms.FirstOrDefault(platform => platform.Id == platformId)?.Number;
    }
}
