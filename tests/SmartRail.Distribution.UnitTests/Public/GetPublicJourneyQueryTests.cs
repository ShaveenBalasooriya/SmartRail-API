using SmartRail.Distribution.Application.Abstractions;
using SmartRail.Distribution.Application.Public;
using SmartRail.Distribution.Application.Public.GetJourney;
using SmartRail.LiveOperations.Contracts;
using SmartRail.SharedKernel.Results;

namespace SmartRail.Distribution.UnitTests.Public;

/// <summary>
/// The journey detail's own rules (passenger-departures spec, journey detail): an empty identifier is
/// refused before anything is read, an unknown journey is a not-found result, and a known one comes
/// back inside the envelope with the response's generation time and refresh interval. The view
/// itself is the one builder's, covered by its own tests (design D1).
/// </summary>
public sealed class GetPublicJourneyQueryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 6, 30, 0, TimeSpan.Zero);

    [Fact]
    public void An_empty_journey_identifier_is_a_validation_failure_keyed_by_the_route_parameter()
    {
        var result = new GetPublicJourneyQueryValidator().Validate(
            new GetPublicJourneyQuery(Guid.Empty));

        Assert.False(result.IsValid);

        var failure = Assert.Single(result.Errors);
        Assert.Equal("id", failure.PropertyName);
    }

    [Fact]
    public void A_journey_identifier_is_accepted()
    {
        var result = new GetPublicJourneyQueryValidator().Validate(
            new GetPublicJourneyQuery(Guid.CreateVersion7()));

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task A_known_journey_comes_back_in_the_envelope_with_its_freshness()
    {
        var journey = PublicJourneyViewBuilder.Build(GuidanceFixture.Journey(), GuidanceFixture.Lookup());
        var handler = Handler(new StubViews(journey));

        var result = await handler.Handle(new GetPublicJourneyQuery(journey.JourneyId), default);

        Assert.True(result.IsSuccess);
        Assert.Same(journey, result.Value.Data);
        Assert.Equal(Now, result.Value.GeneratedAt);
        Assert.Equal(15, result.Value.RefreshAfterSeconds);
    }

    [Fact]
    public async Task An_unknown_journey_is_not_found()
    {
        var handler = Handler(new StubViews(null));

        var result = await handler.Handle(new GetPublicJourneyQuery(Guid.CreateVersion7()), default);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal("journey.not_found", result.Error.Code);
    }

    private static GetPublicJourneyQueryHandler Handler(IPublicJourneyViews views) =>
        new(views, new FixedTimeProvider(Now), new DistributionOptions(15));

    /// <summary>The public view service with one journey in it, or none.</summary>
    private sealed class StubViews(PublicJourneyView? journey) : IPublicJourneyViews
    {
        public Task<PublicJourneyView?> GetAsync(
            Guid journeyId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(journey);

        public Task<IReadOnlyList<PublicJourneyView>> GetByDateAsync(
            DateOnly serviceDate,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PublicJourneyView>>(journey is null ? [] : [journey]);

        public Task<IReadOnlyList<PublicJourneyView>> BuildAsync(
            IReadOnlyList<LiveJourneyView> journeys,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    /// <summary>A clock frozen at one instant, so the envelope's generation time is exact.</summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
