using SmartRail.LiveOperations.Contracts;

namespace SmartRail.Distribution.Application.Streams;

/// <summary>
/// The name a journey event is written under on a stream. The names are the ones the client reads
/// and the ones the operations schema stores, so a stream event and a stored event are named alike
/// (journey-event-stream spec, api-conventions spec).
/// </summary>
public static class StreamEventNames
{
    /// <summary>The frame name for <paramref name="eventType"/>.</summary>
    public static string Of(JourneyEventType eventType) => eventType switch
    {
        JourneyEventType.DepartureLogged => "departure_logged",
        JourneyEventType.DelayPublished => "delay_published",
        JourneyEventType.CancellationPublished => "cancellation_published",
        JourneyEventType.TerminationPublished => "termination_published",
        JourneyEventType.PlatformChanged => "platform_changed",
        JourneyEventType.CorrectionPublished => "correction_published",
        JourneyEventType.JourneyCompleted => "journey_completed",
        _ => throw new ArgumentOutOfRangeException(nameof(eventType), eventType, "Unknown journey event type."),
    };
}
