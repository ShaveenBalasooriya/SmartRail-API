using SmartRail.IdentityAccess.Contracts;
using SmartRail.LiveOperations.Application.Abstractions;
using SmartRail.LiveOperations.Application.LiveJourneys;
using SmartRail.LiveOperations.Domain.LiveState;
using SmartRail.RailNetwork.Contracts;
using SmartRail.SharedKernel.Application.Messaging;
using SmartRail.SharedKernel.Results;

namespace SmartRail.LiveOperations.Application.ControllerView.GetControllerJourney;

/// <summary>One journey with every stop and its published changes (NC-01, NC-06).</summary>
/// <param name="JourneyId">The journey to read. An ID with no live state is not found.</param>
public sealed record GetControllerJourneyQuery(Guid JourneyId) : IQuery<ControllerJourneyDetailResponse>;

internal sealed class GetControllerJourneyQueryHandler(
    ILiveJourneyRepository journeys,
    IStatusChangeReader statusChanges,
    ITimetable timetable,
    INetworkCatalog network,
    IStaffDirectory staff,
    OperationsOptions options,
    TimeProvider timeProvider)
    : IQueryHandler<GetControllerJourneyQuery, ControllerJourneyDetailResponse>
{
    public async Task<Result<ControllerJourneyDetailResponse>> Handle(
        GetControllerJourneyQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var state = await journeys.GetAsync(query.JourneyId, cancellationToken);
        if (state is null)
        {
            return Result.Failure<ControllerJourneyDetailResponse>(JourneyLiveStateErrors.NotFound(query.JourneyId));
        }

        var now = timeProvider.GetUtcNow();
        var threshold = TimeSpan.FromMinutes(options.StaleAfterMinutes);

        var journey = (await timetable.GetScheduledJourneysAsync(state.ServiceDate, cancellationToken))
            .FirstOrDefault(candidate => candidate.Id == state.Id);
        var stations = new StationIndex(await network.GetStationsAsync(cancellationToken));
        var changes = await statusChanges.GetForJourneyAsync(state.Id, cancellationToken);

        var staffIds = changes
            .Select(change => change.PublisherStaffId)
            .Append(state.GuardStaffId)
            .Distinct()
            .ToArray();
        var staffNames = await staff.GetDisplayNamesAsync(staffIds, cancellationToken);

        var detail = ControllerJourneyMapping.ToDetail(
            state,
            journey,
            stations,
            staffNames,
            changes,
            state.IsStaleAt(now, threshold));

        return Result.Success(detail);
    }
}
