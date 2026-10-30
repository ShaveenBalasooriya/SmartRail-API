namespace SmartRail.LiveOperations.Contracts;

/// <summary>Where a journey is in its lifecycle (live-running-state spec).</summary>
public enum JourneyLifecycleStatus
{
    /// <summary>No departure has been recorded and no cancellation is in force.</summary>
    Scheduled,

    /// <summary>At least one departure has been recorded.</summary>
    Running,

    /// <summary>An uncorrected cancellation is in force.</summary>
    Cancelled,

    /// <summary>
    /// An uncorrected termination is in force: the train ended early, before its terminus (design
    /// D7).
    /// </summary>
    Terminated,

    /// <summary>The penultimate calling stop has departed and the terminus arrival has passed.</summary>
    Completed,
}
