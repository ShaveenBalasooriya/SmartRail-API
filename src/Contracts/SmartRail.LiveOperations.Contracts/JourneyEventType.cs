namespace SmartRail.LiveOperations.Contracts;

/// <summary>
/// The kinds of change a journey event records. These are exactly the types the event stream
/// accepts (journey-event-stream spec); the <c>operations.journey_events</c> check constraint
/// enforces the same list.
/// </summary>
public enum JourneyEventType
{
    /// <summary>The Guard recorded the actual departure at a stop.</summary>
    DepartureLogged,

    /// <summary>The Network Controller published a delay.</summary>
    DelayPublished,

    /// <summary>The Network Controller cancelled the journey.</summary>
    CancellationPublished,

    /// <summary>The Network Controller ended the journey early (design D7).</summary>
    TerminationPublished,

    /// <summary>The Network Controller changed a stop's platform.</summary>
    PlatformChanged,

    /// <summary>The Network Controller corrected an earlier change.</summary>
    CorrectionPublished,

    /// <summary>The journey reached its terminus and was completed automatically.</summary>
    JourneyCompleted,
}
