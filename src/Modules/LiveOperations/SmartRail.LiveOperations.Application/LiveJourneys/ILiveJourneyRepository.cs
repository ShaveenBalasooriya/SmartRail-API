using SmartRail.LiveOperations.Domain.LiveState;

namespace SmartRail.LiveOperations.Application.LiveJourneys;

/// <summary>
/// The reads the live-journey queries need. The implementation projects from the module's own
/// schema; the handlers map the aggregate to the public contract (design D4).
/// </summary>
public interface ILiveJourneyRepository
{
    /// <summary>One journey with its live stops, or <see langword="null"/> when the ID is unknown.</summary>
    Task<JourneyLiveState?> GetAsync(Guid journeyId, CancellationToken cancellationToken = default);

    /// <summary>Every journey on one railway calendar day, ordered by scheduled departure.</summary>
    Task<IReadOnlyList<JourneyLiveState>> GetByDateAsync(
        DateOnly serviceDate,
        CancellationToken cancellationToken = default);
}
