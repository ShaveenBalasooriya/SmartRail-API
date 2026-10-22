using SmartRail.LiveOperations.Application.LiveState;
using SmartRail.LiveOperations.Contracts;
using SmartRail.SharedKernel.Application.Messaging;
using SmartRail.SharedKernel.Results;

namespace SmartRail.LiveOperations.Application.LiveJourneys.GetLiveJourneysByDate;

/// <summary>Every journey's live picture on one railway calendar day.</summary>
public sealed record GetLiveJourneysByDateQuery(DateOnly ServiceDate)
    : IQuery<IReadOnlyList<LiveJourneyView>>;

internal sealed class GetLiveJourneysByDateQueryHandler(ILiveJourneyRepository repository)
    : IQueryHandler<GetLiveJourneysByDateQuery, IReadOnlyList<LiveJourneyView>>
{
    public async Task<Result<IReadOnlyList<LiveJourneyView>>> Handle(
        GetLiveJourneysByDateQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var states = await repository.GetByDateAsync(query.ServiceDate, cancellationToken);

        return Result.Success<IReadOnlyList<LiveJourneyView>>(
            [.. states.Select(LiveStateMapping.ToView)]);
    }
}
