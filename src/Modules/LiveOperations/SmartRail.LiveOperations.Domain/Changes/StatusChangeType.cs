namespace SmartRail.LiveOperations.Domain.Changes;

/// <summary>
/// The kinds of change a Network Controller publishes on today's journeys (status-publication and
/// change-correction specs). A correction is a row of its own that withdraws another change; it
/// carries no replacement values (design D3).
/// </summary>
public enum StatusChangeType
{
    /// <summary>A delay in whole minutes from a calling stop that has not yet departed.</summary>
    Delay,

    /// <summary>The journey is cancelled from end to end.</summary>
    Cancellation,

    /// <summary>A running journey ends at the last stopping point the train served (design D7).</summary>
    Termination,

    /// <summary>The departure platform at one calling stop changes.</summary>
    PlatformChange,

    /// <summary>Withdraws another published change without erasing it (design D3).</summary>
    Correction,
}
