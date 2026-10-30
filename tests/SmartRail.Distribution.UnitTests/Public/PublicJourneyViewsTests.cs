using SmartRail.Distribution.Application.Public;
using SmartRail.Distribution.Application.Public.Guidance;
using SmartRail.LiveOperations.Contracts;

namespace SmartRail.Distribution.UnitTests.Public;

/// <summary>
/// How the public read fills guidance (design D1, D6): every item it returns carries the guidance for
/// its own disruption, and the day's journeys are read only when some item needs a next service and
/// never more than once.
/// </summary>
public sealed class PublicJourneyViewsTests
{
    [Fact]
    public async Task Reading_one_day_fills_guidance_for_each_item_without_reading_the_day_again()
    {
        var cancelled = CancelledJourney();
        var next = GuidanceFixture.Candidate(GuidanceFixture.At(8, 20), GuidanceFixture.At(8, 25));
        var live = new CountingLiveJourneys(cancelled, next);

        var views = await Views(live).GetByDateAsync(GuidanceFixture.ServiceDate);

        Assert.Equal(1, live.DayReads);
        Assert.Equal(2, views.Count);

        var cancelledView = Assert.Single(views, view => view.JourneyId == cancelled.JourneyId);
        Assert.NotNull(cancelledView.Guidance);
        Assert.Equal(PublicGuidanceType.Cancellation, cancelledView.Guidance.Type);
        Assert.Equal(next.JourneyId, cancelledView.Guidance.NextService?.JourneyId);

        var undisruptedView = Assert.Single(views, view => view.JourneyId == next.JourneyId);
        Assert.Null(undisruptedView.Guidance);
    }

    [Fact]
    public async Task Reading_one_undisrupted_journey_does_not_read_the_day()
    {
        var journey = GuidanceFixture.Journey();
        var live = new CountingLiveJourneys(journey);

        var view = await Views(live).GetAsync(journey.JourneyId);

        Assert.NotNull(view);
        Assert.Null(view.Guidance);
        Assert.Equal(0, live.DayReads);
    }

    [Fact]
    public async Task Reading_one_cancelled_journey_reads_the_day_once_to_find_the_next_service()
    {
        var cancelled = CancelledJourney();
        var next = GuidanceFixture.Candidate(GuidanceFixture.At(8, 20), GuidanceFixture.At(8, 25));
        var live = new CountingLiveJourneys(cancelled, next);

        var view = await Views(live).GetAsync(cancelled.JourneyId);

        Assert.Equal(1, live.DayReads);
        Assert.Equal(next.JourneyId, view?.Guidance?.NextService?.JourneyId);
    }

    private static LiveJourneyView CancelledJourney() =>
        GuidanceFixture.Journey(
            status: JourneyLifecycleStatus.Cancelled,
            stops: GuidanceFixture.CancelledStops());

    private static PublicJourneyViews Views(ILiveJourneys live) =>
        new(live, new FixtureLookups(), GuidanceFixture.RailwayZone);

    private sealed class FixtureLookups : INetworkLookupProvider
    {
        public Task<NetworkLookup> GetAsync(
            IReadOnlyCollection<Guid> serviceIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(GuidanceFixture.Lookup());
    }

    /// <summary>The live state a test supplies, counting how often the day was read.</summary>
    private sealed class CountingLiveJourneys(params LiveJourneyView[] journeys) : ILiveJourneys
    {
        public int DayReads { get; private set; }

        public Task<LiveJourneyView?> GetAsync(
            Guid journeyId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(journeys.FirstOrDefault(journey => journey.JourneyId == journeyId));

        public Task<IReadOnlyList<LiveJourneyView>> GetByDateAsync(
            DateOnly serviceDate,
            CancellationToken cancellationToken = default)
        {
            DayReads++;

            return Task.FromResult<IReadOnlyList<LiveJourneyView>>(
                journeys.Where(journey => journey.ServiceDate == serviceDate).ToArray());
        }

        public Task<IReadOnlyList<JourneyEventView>> GetEventsAfterAsync(
            long position,
            Guid? journeyId = null,
            int limit = 100,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<long?> GetOldestRetainedPositionAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
