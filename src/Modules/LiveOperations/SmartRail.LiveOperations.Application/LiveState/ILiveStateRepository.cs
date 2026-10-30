using SmartRail.LiveOperations.Domain.LiveState;

namespace SmartRail.LiveOperations.Application.LiveState;

/// <summary>
/// The persistence the live-state use cases need. Raw SQL — the row lock and the idempotent creation
/// inserts — lives only in the repository implementation (design D4).
/// </summary>
public interface ILiveStateRepository
{
    /// <summary>
    /// Locks the journey's live-state row with <c>SELECT … FOR UPDATE</c> and loads the aggregate
    /// with its stops, or returns <see langword="null"/> when the journey has no live state. The
    /// caller must already have opened the transaction through <see cref="Abstractions.IUnitOfWork"/>.
    /// </summary>
    Task<JourneyLiveState?> GetForUpdateAsync(Guid journeyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts the state and its stop rows with <c>ON CONFLICT DO NOTHING</c>. Returns true when this
    /// call created them and false when another creator got there first.
    /// </summary>
    Task<bool> CreateIfMissingAsync(JourneyLiveState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// The identifiers of the journeys whose lifecycle status is <c>running</c>, in identifier order
    /// and with no change tracking. Only a running journey can complete, so a terminated, cancelled
    /// or completed one is never returned and never touched (design D5).
    /// </summary>
    Task<IReadOnlyList<Guid>> GetRunningJourneyIdsAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds the event to the change tracker; it is written by the next save.</summary>
    void Append(JourneyEvent journeyEvent);
}
