using SmartRail.LiveOperations.Contracts;

namespace SmartRail.Catering.UnitTests.TestDoubles;

/// <summary>Live journey reads backed by one test-owned list.</summary>
internal sealed class StubLiveJourneys(params LiveJourneyView[] journeys) : ILiveJourneys
{
    private readonly IReadOnlyList<LiveJourneyView> _journeys = journeys;

    public List<DateOnly> RequestedDates { get; } = [];

    public int GetByDateCount { get; private set; }

    public Task<LiveJourneyView?> GetAsync(Guid journeyId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_journeys.FirstOrDefault(journey => journey.JourneyId == journeyId));

    public Task<IReadOnlyList<LiveJourneyView>> GetByDateAsync(
        DateOnly serviceDate,
        CancellationToken cancellationToken = default)
    {
        GetByDateCount++;
        RequestedDates.Add(serviceDate);
        return Task.FromResult<IReadOnlyList<LiveJourneyView>>(
            [.. _journeys.Where(journey => journey.ServiceDate == serviceDate)]);
    }

    public Task<IReadOnlyList<JourneyEventView>> GetEventsAfterAsync(
        long position,
        Guid? journeyId = null,
        int limit = 100,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The station journeys query does not read the event stream.");

    public Task<long?> GetOldestRetainedPositionAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The station journeys query does not read retention information.");
}
