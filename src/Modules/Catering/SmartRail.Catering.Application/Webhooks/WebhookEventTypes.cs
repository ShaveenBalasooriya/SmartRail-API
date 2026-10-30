using System.Text.Json;
using SmartRail.Catering.Domain.Webhooks;
using SmartRail.LiveOperations.Contracts;

namespace SmartRail.Catering.Application.Webhooks;

/// <summary>
/// Decides which journey events are pushed to a provider. The five change types are pushed so the
/// kitchen learns a train will not reach its later stations; a departure or a completed journey is
/// not, which is why a provider sees gaps in the sequence numbers (webhook-delivery spec, "Pushed
/// event types"; design D5).
/// </summary>
public static class WebhookEventTypes
{
    /// <summary>
    /// Maps a journey event type onto the pushed type, or returns <see langword="false"/> when the
    /// event is not pushed.
    /// </summary>
    public static bool TryMap(JourneyEventType journeyEventType, out WebhookEventType webhookEventType)
    {
        switch (journeyEventType)
        {
            case JourneyEventType.DelayPublished:
                webhookEventType = WebhookEventType.DelayPublished;
                return true;
            case JourneyEventType.CancellationPublished:
                webhookEventType = WebhookEventType.CancellationPublished;
                return true;
            case JourneyEventType.TerminationPublished:
                webhookEventType = WebhookEventType.TerminationPublished;
                return true;
            case JourneyEventType.PlatformChanged:
                webhookEventType = WebhookEventType.PlatformChanged;
                return true;
            case JourneyEventType.CorrectionPublished:
                webhookEventType = WebhookEventType.CorrectionPublished;
                return true;
            default:
                webhookEventType = default;
                return false;
        }
    }

    /// <summary>The snake-case name the payload and the deliveries table use.</summary>
    public static string ToStorageValue(WebhookEventType eventType) => eventType switch
    {
        WebhookEventType.DelayPublished => "delay_published",
        WebhookEventType.CancellationPublished => "cancellation_published",
        WebhookEventType.TerminationPublished => "termination_published",
        WebhookEventType.PlatformChanged => "platform_changed",
        WebhookEventType.CorrectionPublished => "correction_published",
        _ => throw new ArgumentOutOfRangeException(
            nameof(eventType),
            eventType,
            "Unknown webhook event type."),
    };

    /// <summary>The version of the provider payload this module writes (design D5).</summary>
    public const int PayloadVersion = 1;

    /// <summary>The JSON serializer the payload is written with: web defaults, so property names are camel-case.</summary>
    public static readonly JsonSerializerOptions PayloadJsonOptions = new(JsonSerializerDefaults.Web);
}
