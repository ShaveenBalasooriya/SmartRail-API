using SmartRail.LiveOperations.Application.LiveJourneys;
using SmartRail.LiveOperations.Domain.LiveState;

namespace SmartRail.LiveOperations.UnitTests.TestDoubles;

/// <summary>An in-memory read repository for controller journey queries.</summary>
internal sealed class FakeLiveJourneyRepository(params JourneyLiveState[] states) : ILiveJourneyRepository
{
    private readonly JourneyLiveState[] _states = states;

    public Task<JourneyLiveState?> GetAsync(Guid journeyId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_states.SingleOrDefault(state => state.Id == journeyId));

    public Task<IReadOnlyList<JourneyLiveState>> GetByDateAsync(
        DateOnly serviceDate,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<JourneyLiveState> result = [.. _states.Where(state => state.ServiceDate == serviceDate)];
        return Task.FromResult(result);
    }
}
