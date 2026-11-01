using SmartRail.Distribution.Application.Public;
using SmartRail.Distribution.UnitTests.Public;
using SmartRail.LiveOperations.Contracts;

namespace SmartRail.Distribution.UnitTests.TestDoubles;

/// <summary>
/// Builds the public view of each stored snapshot with the real view builder, and records the batches
/// it was asked for so a test can check the catch-up built the payloads once per batch.
/// </summary>
internal sealed class StubPublicJourneyViews : IPublicJourneyViews
{
    internal List<IReadOnlyList<LiveJourneyView>> Batches { get; } = [];

    public Task<IReadOnlyList<PublicJourneyView>> BuildAsync(
        IReadOnlyList<LiveJourneyView> journeys,
        CancellationToken cancellationToken = default)
    {
        Batches.Add(journeys);

        var lookup = PublicJourneyViewFixture.Lookup();

        return Task.FromResult<IReadOnlyList<PublicJourneyView>>(
            [.. journeys.Select(journey => PublicJourneyViewBuilder.Build(journey, lookup))]);
    }

    public Task<PublicJourneyView?> GetAsync(Guid journeyId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The catch-up builds the views of the events it read.");

    public Task<IReadOnlyList<PublicJourneyView>> GetByDateAsync(
        DateOnly serviceDate,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The catch-up builds the views of the events it read.");
}
