using SmartRail.LiveOperations.Contracts;

namespace SmartRail.Distribution.UnitTests.TestDoubles;

/// <summary>
/// An in-memory journey event stream. It answers with the stored events after a position, in
/// position order and bounded by the batch size, and records every read so a test can see how the
/// catch-up paged.
/// </summary>
internal sealed class FakeLiveJourneys(params JourneyEventView[] storedEvents) : ILiveJourneys
{
    private readonly List<JourneyEventView> _events = [.. storedEvents.OrderBy(stored => stored.StreamPosition)];

    internal List<Read> Reads { get; } = [];

    /// <summary>When set, the stream fails instead of answering, so a broken dependency is observable.</summary>
    internal Exception? ExceptionToThrow { get; set; }

    public Task<long?> GetOldestRetainedPositionAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfFailing();

        return Task.FromResult(_events.Count == 0 ? (long?)null : _events[0].StreamPosition);
    }

    public Task<IReadOnlyList<JourneyEventView>> GetEventsAfterAsync(
        long position,
        Guid? journeyId = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        ThrowIfFailing();
        Reads.Add(new Read(position, journeyId, limit));

        return Task.FromResult<IReadOnlyList<JourneyEventView>>(
        [
            .. _events
                .Where(stored => stored.StreamPosition > position)
                .Where(stored => journeyId is null || stored.JourneyId == journeyId)
                .Take(limit),
        ]);
    }

    public Task<LiveJourneyView?> GetAsync(Guid journeyId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The catch-up reads the stream, not one journey.");

    public Task<IReadOnlyList<LiveJourneyView>> GetByDateAsync(
        DateOnly serviceDate,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The catch-up reads the stream, not a day.");

    private void ThrowIfFailing()
    {
        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }
    }

    /// <summary>One read the catch-up asked for.</summary>
    internal sealed record Read(long Position, Guid? JourneyId, int Limit);
}
