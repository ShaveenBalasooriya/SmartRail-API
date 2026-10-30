namespace SmartRail.LiveOperations.Domain.Calculation;

/// <summary>
/// A recorded fact that can affect a journey's live state. <see cref="StreamOrder"/> is the order
/// the fact was committed in, which is what "most recent" means for the calculator: wall-clock
/// times would make the same facts compute differently on different runs.
/// </summary>
public abstract record Fact(long StreamOrder);

/// <summary>
/// A Guard's departure log at one stop. <see cref="DepartedAt"/> is the device time at the tap and
/// <see cref="RecordedAt"/> is the server time the log was received, so a log that synced late
/// still reports the original departure.
/// </summary>
public sealed record DepartureFact(
    int StopSequence,
    DateTimeOffset DepartedAt,
    DateTimeOffset RecordedAt,
    long StreamOrder) : Fact(StreamOrder);

/// <summary>
/// A Network Controller change. A corrected fact has been withdrawn and is filtered out before
/// computing, so a correction leaves the state as if the original had never been published.
/// </summary>
public sealed record ChangeFact(
    ChangeKind Kind,
    int? StopSequence,
    int? DelayMinutes,
    Guid? PlatformId,
    DateTimeOffset PublishedAt,
    long StreamOrder,
    bool Corrected) : Fact(StreamOrder);

/// <summary>The changes a Network Controller can publish in MVP 1.</summary>
public enum ChangeKind
{
    /// <summary>A delay in whole minutes from <see cref="ChangeFact.StopSequence"/> onward.</summary>
    Delay,

    /// <summary>The journey is cancelled from end to end.</summary>
    Cancellation,

    /// <summary>
    /// The journey ends early at <see cref="ChangeFact.StopSequence"/>, the last stop the train
    /// served (design D7).
    /// </summary>
    Termination,

    /// <summary>The departure platform at <see cref="ChangeFact.StopSequence"/> changes.</summary>
    PlatformChange,
}
