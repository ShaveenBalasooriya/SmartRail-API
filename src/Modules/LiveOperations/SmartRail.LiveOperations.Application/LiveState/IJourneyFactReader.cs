using SmartRail.LiveOperations.Domain.Calculation;

namespace SmartRail.LiveOperations.Application.LiveState;

/// <summary>
/// Reads every fact recorded for one journey, in any order: the calculator orders them itself by
/// the order they were committed in (design D1). The departure-log and status-change changes
/// replace this change's empty implementation with one that reads their tables.
/// </summary>
public interface IJourneyFactReader
{
    /// <summary>Every fact recorded for the journey, including one the caller has just stored.</summary>
    Task<IReadOnlyList<Fact>> GetFactsAsync(Guid journeyId, CancellationToken cancellationToken = default);
}
