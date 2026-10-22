using SmartRail.LiveOperations.Domain.LiveState;

namespace SmartRail.LiveOperations.Application.JourneyEvents;

/// <summary>The reads of the append-only journey event stream (design D4).</summary>
public interface IJourneyEventRepository
{
    /// <summary>
    /// The events whose stream position is greater than <paramref name="position"/>, in position
    /// order, optionally restricted to one journey.
    /// </summary>
    Task<IReadOnlyList<JourneyEvent>> GetAfterAsync(
        long position,
        Guid? journeyId,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>The lowest stream position still retained, or <see langword="null"/> when none exists.</summary>
    Task<long?> GetOldestPositionAsync(CancellationToken cancellationToken = default);
}
