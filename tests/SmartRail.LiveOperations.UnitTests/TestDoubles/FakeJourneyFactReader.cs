using SmartRail.LiveOperations.Application.LiveState;
using SmartRail.LiveOperations.Domain.Calculation;

namespace SmartRail.LiveOperations.UnitTests.TestDoubles;

/// <summary>Returns canned facts for each journey and records the reads.</summary>
internal sealed class FakeJourneyFactReader : IJourneyFactReader
{
    private readonly Dictionary<Guid, IReadOnlyList<Fact>> _facts = new Dictionary<Guid, IReadOnlyList<Fact>>();

    public List<Guid> RequestedJourneyIds { get; } = [];

    public Exception? ExceptionToThrow { get; set; }

    public void SetFacts(Guid journeyId, params Fact[] facts) => _facts[journeyId] = facts;

    public Task<IReadOnlyList<Fact>> GetFactsAsync(
        Guid journeyId,
        CancellationToken cancellationToken = default)
    {
        RequestedJourneyIds.Add(journeyId);
        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        return Task.FromResult(_facts.GetValueOrDefault(journeyId) ?? []);
    }
}
