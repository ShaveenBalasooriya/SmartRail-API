using SmartRail.LiveOperations.Application.LiveState;
using SmartRail.LiveOperations.Contracts;
using SmartRail.SharedKernel.Application.Messaging;
using SmartRail.SharedKernel.Results;

namespace SmartRail.LiveOperations.Application.LiveJourneys.GetLiveJourney;

/// <summary>One journey's live picture, or <see langword="null"/> when the ID is unknown.</summary>
public sealed record GetLiveJourneyQuery(Guid JourneyId) : IQuery<LiveJourneyView?>;

internal sealed class GetLiveJourneyQueryHandler(ILiveJourneyRepository repository)
    : IQueryHandler<GetLiveJourneyQuery, LiveJourneyView?>
{
    public async Task<Result<LiveJourneyView?>> Handle(
        GetLiveJourneyQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var state = await repository.GetAsync(query.JourneyId, cancellationToken);

        return Result.Success(state is null ? null : LiveStateMapping.ToView(state));
    }
}
