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

    /// <summary>
    /// What each of the given correction changes withdraws, keyed by the correction's own identifier.
    /// It comes from the stored rows — the correction names the original change and the original's
    /// event names that change as its source — so the event stream needs no extra column
    /// (change-correction spec "Event references original").
    /// </summary>
    Task<IReadOnlyDictionary<Guid, CorrectionReference>> GetCorrectionReferencesAsync(
        IReadOnlyCollection<Guid> correctionChangeIds,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// One correction's link to what it withdraws.
/// </summary>
/// <param name="CorrectsChangeId">The change the correction withdraws.</param>
/// <param name="CorrectsEventId">
/// The event that recorded that original change, or <see langword="null"/> when the change appended
/// no event because it did not alter the journey's live state.
/// </param>
public sealed record CorrectionReference(Guid CorrectsChangeId, Guid? CorrectsEventId);
