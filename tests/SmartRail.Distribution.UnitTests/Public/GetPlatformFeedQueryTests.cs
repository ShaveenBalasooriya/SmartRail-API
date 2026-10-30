using SmartRail.Distribution.Application.Abstractions;
using SmartRail.Distribution.Application.Public;
using SmartRail.Distribution.Application.Public.Display;
using SmartRail.LiveOperations.Contracts;
using SmartRail.SharedKernel.Results;

namespace SmartRail.Distribution.UnitTests.Public;

/// <summary>
/// The display feed's own rules (station-display-feed spec): a count outside one to twenty is a
/// validation failure, an unknown station or an unknown platform number is a not-found result, a
/// missing count means eight, and a known platform comes back inside the envelope with the
/// response's generation time and refresh interval. The placement itself is the selection's, covered
/// by its own tests.
/// </summary>
public sealed class GetPlatformFeedQueryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 7, 50, 0, TimeSpan.Zero);

    private static readonly Guid Alpha = PublicJourneyViewFixture.Alpha;
    private static readonly Guid Gamma = PublicJourneyViewFixture.Gamma;
    private static readonly Guid PlatformOne = PublicJourneyViewFixture.AlphaPlatformOne;

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(20)]
    public void A_missing_count_and_the_bounds_are_accepted(int? count)
    {
        var result = new GetPlatformFeedQueryValidator().Validate(Query(count));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public void A_count_outside_one_to_twenty_is_refused_and_keyed_by_count(int count)
    {
        var result = new GetPlatformFeedQueryValidator().Validate(Query(count));

        Assert.False(result.IsValid);

        var failure = Assert.Single(result.Errors);
        Assert.Equal("count", failure.PropertyName);
    }

    [Fact]
    public async Task A_known_platform_comes_back_in_the_envelope_with_a_default_count_of_eight()
    {
        var journeys = NineJourneys();
        var handler = Handler(journeys);

        var result = await handler.Handle(Query(null), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(Now, result.Value.GeneratedAt);
        Assert.Equal(15, result.Value.RefreshAfterSeconds);
        Assert.Equal(8, result.Value.Data.Departures.Count);
        Assert.All(result.Value.Data.Departures, departure => Assert.Equal("1", departure.Origin.Platform?.Number));
    }

    [Fact]
    public async Task An_unknown_station_is_not_found()
    {
        var result = await Handler(NineJourneys()).Handle(
            new GetPlatformFeedQuery(Guid.CreateVersion7(), "1", null),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal("station.not_found", result.Error.Code);
    }

    [Fact]
    public async Task A_platform_number_the_station_does_not_have_is_not_found()
    {
        var result = await Handler(NineJourneys()).Handle(
            new GetPlatformFeedQuery(Alpha, "9", null),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal("platform.not_found", result.Error.Code);
    }

    private static GetPlatformFeedQuery Query(int? count) => new(Alpha, "1", count);

    private static GetPlatformFeedQueryHandler Handler(IReadOnlyList<PublicJourneyView> journeys) =>
        new(
            new StubViews(journeys),
            new StubLookups(PublicJourneyViewFixture.Lookup()),
            new FixedTimeProvider(Now),
            TimeZoneInfo.Utc,
            new DistributionOptions(15));

    /// <summary>Nine departures on platform 1, all inside the station's two-hour window.</summary>
    private static IReadOnlyList<PublicJourneyView> NineJourneys() =>
        [.. Enumerable.Range(0, 9).Select(index => PublicJourneyViewBuilder.Build(
            PublicJourneyViewFixture.Journey(
                stops:
                [
                    new LiveStopView(
                        1,
                        Alpha,
                        IsCalling: true,
                        ScheduledArrival: null,
                        ScheduledDeparture: Now.AddMinutes(10 + index),
                        ExpectedArrival: null,
                        ExpectedDeparture: Now.AddMinutes(10 + index),
                        ActualDeparture: null,
                        PlatformId: PlatformOne,
                        PlatformChangedAt: null),
                    new LiveStopView(
                        2,
                        Gamma,
                        IsCalling: true,
                        ScheduledArrival: Now.AddMinutes(60),
                        ScheduledDeparture: null,
                        ExpectedArrival: null,
                        ExpectedDeparture: null,
                        ActualDeparture: null,
                        PlatformId: null,
                        PlatformChangedAt: null),
                ],
                journeyId: Guid.CreateVersion7()),
            PublicJourneyViewFixture.Lookup()))];

    /// <summary>The public read service with one day's journeys.</summary>
    private sealed class StubViews(IReadOnlyList<PublicJourneyView> journeys) : IPublicJourneyViews
    {
        public Task<PublicJourneyView?> GetAsync(
            Guid journeyId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(journeys.SingleOrDefault(journey => journey.JourneyId == journeyId));

        public Task<IReadOnlyList<PublicJourneyView>> GetByDateAsync(
            DateOnly serviceDate,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(journeys);

        public Task<IReadOnlyList<PublicJourneyView>> BuildAsync(
            IReadOnlyList<LiveJourneyView> journeys,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    /// <summary>The catalogue and the timetable the handler reads once.</summary>
    private sealed class StubLookups(NetworkLookup lookup) : INetworkLookupProvider
    {
        public Task<NetworkLookup> GetAsync(
            IReadOnlyCollection<Guid> serviceIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(lookup);
    }

    /// <summary>A clock frozen at one instant, so the envelope's generation time is exact.</summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
