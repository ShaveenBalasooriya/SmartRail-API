namespace SmartRail.Catering.Domain.Webhooks;

/// <summary>
/// The event types pushed to a provider: delay, cancellation, early termination, platform change and
/// correction. A departure or a completed journey is not pushed, so a provider sees gaps in the
/// journey's sequence numbers (webhook-delivery spec, design D5).
/// </summary>
public enum WebhookEventType
{
    /// <summary>A delay was published for a journey.</summary>
    DelayPublished,

    /// <summary>A journey was cancelled end to end.</summary>
    CancellationPublished,

    /// <summary>A running journey was ended at its last served stop.</summary>
    TerminationPublished,

    /// <summary>A calling stop moved to another platform.</summary>
    PlatformChanged,

    /// <summary>A published change was withdrawn.</summary>
    CorrectionPublished,
}
