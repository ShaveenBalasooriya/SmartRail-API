using SmartRail.LiveOperations.Application.LiveState;
using SmartRail.LiveOperations.Domain.LiveState;

namespace SmartRail.LiveOperations.UnitTests.TestDoubles;

/// <summary>An in-memory live-state repository with optional dependency failures.</summary>
internal sealed class FakeLiveStateRepository : ILiveStateRepository
{
    private readonly Dictionary<Guid, JourneyLiveState> _states;
    private readonly List<Guid> _runningJourneyIds;

    public FakeLiveStateRepository(params JourneyLiveState[] states)
    {
        _states = states.ToDictionary(state => state.Id);
        _runningJourneyIds = [.. states
            .Where(state => state.Status == JourneyLifecycleStatus.Running)
            .Select(state => state.Id)
            .Order()];
    }

    public List<JourneyEvent> AppendedEvents { get; } = [];

    public IReadOnlyCollection<JourneyLiveState> States => _states.Values;

    public FakeUnitOfWork? UnitOfWork { get; set; }

    public Exception? GetForUpdateException { get; set; }

    public Exception? CreateException { get; set; }

    public Task<JourneyLiveState?> GetForUpdateAsync(
        Guid journeyId,
        CancellationToken cancellationToken = default)
    {
        if (GetForUpdateException is not null)
        {
            throw GetForUpdateException;
        }

        return Task.FromResult(_states.GetValueOrDefault(journeyId));
    }

    public Task<bool> CreateIfMissingAsync(
        JourneyLiveState state,
        CancellationToken cancellationToken = default)
    {
        if (CreateException is not null)
        {
            throw CreateException;
        }

        if (!_states.TryAdd(state.Id, state))
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<Guid>> GetRunningJourneyIdsAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Guid>>(_runningJourneyIds);

    public void Append(JourneyEvent journeyEvent)
    {
        AppendedEvents.Add(journeyEvent);
        UnitOfWork?.RegisterRollback(() => AppendedEvents.Remove(journeyEvent));
    }
}
