namespace SmartRail.LiveOperations.Domain.LiveState;

/// <summary>
/// Where a journey is in its lifecycle (live-running-state spec). The public contract keeps its
/// own enum so the module's API does not depend on its domain; Application maps between the two.
/// </summary>
public enum JourneyLifecycleStatus
{
    /// <summary>No departure has been recorded and no cancellation is in force.</summary>
    Scheduled,

    /// <summary>At least one departure has been recorded.</summary>
    Running,

    /// <summary>An uncorrected cancellation is in force.</summary>
    Cancelled,

    /// <summary>The penultimate calling stop has departed and the terminus arrival has passed.</summary>
    Completed,
}
