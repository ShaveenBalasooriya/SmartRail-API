using MediatR;
using SmartRail.LiveOperations.Application.LiveJourneys.GetLiveJourney;
using SmartRail.LiveOperations.Application.LiveJourneys.GetLiveJourneysByDate;
using SmartRail.LiveOperations.Application.JourneyEvents.GetEventsAfter;
using SmartRail.LiveOperations.Application.JourneyEvents.GetOldestPosition;
using SmartRail.LiveOperations.Contracts;

namespace SmartRail.LiveOperations.Application.LiveJourneys;

/// <summary>
/// The module's in-process read contract, implemented by sending the module's own queries, so
/// another module's call and an HTTP request run through the same handler.
/// </summary>
internal sealed class LiveJourneysContract(ISender sender) : ILiveJourneys
{
    public async Task<LiveJourneyView?> GetAsync(
        Guid journeyId,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(new GetLiveJourneyQuery(journeyId), cancellationToken);
        return result.Value;
    }

    public async Task<IReadOnlyList<LiveJourneyView>> GetByDateAsync(
        DateOnly serviceDate,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(new GetLiveJourneysByDateQuery(serviceDate), cancellationToken);
        return result.Value;
    }

    public async Task<IReadOnlyList<JourneyEventView>> GetEventsAfterAsync(
        long position,
        Guid? journeyId = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(
            new GetEventsAfterQuery(position, journeyId, limit),
            cancellationToken);

        return result.Value;
    }

    public async Task<long?> GetOldestRetainedPositionAsync(CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(new GetOldestPositionQuery(), cancellationToken);
        return result.Value;
    }
}
