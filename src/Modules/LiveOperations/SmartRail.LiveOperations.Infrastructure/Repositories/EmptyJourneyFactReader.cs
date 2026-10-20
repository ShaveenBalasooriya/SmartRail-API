using SmartRail.LiveOperations.Application.LiveState;
using SmartRail.LiveOperations.Domain.Calculation;

namespace SmartRail.LiveOperations.Infrastructure.Repositories;

/// <summary>
/// The fact reader this change registers: the departure-log and status-change tables arrive with
/// the next changes, so until then a journey has no recorded facts. Those changes replace this
/// registration with a reader for their own tables.
/// </summary>
internal sealed class EmptyJourneyFactReader : IJourneyFactReader
{
    public Task<IReadOnlyList<Fact>> GetFactsAsync(
        Guid journeyId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<Fact>>([]);
    }
}
