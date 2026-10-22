namespace SmartRail.LiveOperations.Domain.LiveState;

/// <summary>
/// The kinds of change a journey event records. These are exactly the six types the event stream
/// accepts (journey-event-stream spec). The public contract keeps its own enum; Application maps
/// between the two.
/// </summary>
public enum JourneyEventType
{
    /// <summary>The Guard recorded the actual departure at a stop.</summary>
    DepartureLogged,

    /// <summary>The Network Controller published a delay.</summary>
    DelayPublished,

    /// <summary>The Network Controller cancelled the journey.</summary>
    CancellationPublished,

    /// <summary>The Network Controller changed a stop's platform.</summary>
    PlatformChanged,

    /// <summary>The Network Controller corrected an earlier change.</summary>
    CorrectionPublished,

    /// <summary>The journey reached its terminus and was completed automatically.</summary>
    JourneyCompleted,
}
