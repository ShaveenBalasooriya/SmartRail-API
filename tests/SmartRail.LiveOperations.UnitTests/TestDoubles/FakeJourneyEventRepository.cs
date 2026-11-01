using SmartRail.LiveOperations.Application.JourneyEvents;
using SmartRail.LiveOperations.Domain.LiveState;

namespace SmartRail.LiveOperations.UnitTests.TestDoubles;

/// <summary>Returns canned journey events and records catch-up queries.</summary>
internal sealed class FakeJourneyEventRepository : IJourneyEventRepository
{
    public IReadOnlyList<JourneyEvent> EventsToReturn { get; set; } = [];

    public IReadOnlyDictionary<Guid, CorrectionReference> CorrectionsToReturn { get; set; } =
        new Dictionary<Guid, CorrectionReference>();

    public List<(long Position, Guid? JourneyId, int Limit)> Queries { get; } = [];

    public List<IReadOnlyCollection<Guid>> CorrectionRequests { get; } = [];

    public Exception? ExceptionToThrow { get; set; }

    public Task<IReadOnlyList<JourneyEvent>> GetAfterAsync(
        long position,
        Guid? journeyId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        Queries.Add((position, journeyId, limit));
        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        return Task.FromResult(EventsToReturn);
    }

    public Task<IReadOnlyDictionary<Guid, CorrectionReference>> GetCorrectionReferencesAsync(
        IReadOnlyCollection<Guid> correctionChangeIds,
        CancellationToken cancellationToken = default)
    {
        CorrectionRequests.Add(correctionChangeIds);
        return Task.FromResult(CorrectionsToReturn);
    }

    public Task<long?> GetOldestPositionAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This query only reads events after a position.");
}
