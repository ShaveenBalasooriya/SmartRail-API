using SmartRail.LiveOperations.Application.ControllerView;

namespace SmartRail.LiveOperations.UnitTests.TestDoubles;

/// <summary>Returns a canned controller history and records the requested journey.</summary>
internal sealed class StubStatusChangeReader : IStatusChangeReader
{
    public IReadOnlyList<StatusChangeView> ChangesToReturn { get; set; } = [];

    public List<Guid> RequestedJourneyIds { get; } = [];

    public Exception? ExceptionToThrow { get; set; }

    public Task<IReadOnlyList<StatusChangeView>> GetForJourneyAsync(
        Guid journeyId,
        CancellationToken cancellationToken = default)
    {
        RequestedJourneyIds.Add(journeyId);
        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        return Task.FromResult(ChangesToReturn);
    }
}
