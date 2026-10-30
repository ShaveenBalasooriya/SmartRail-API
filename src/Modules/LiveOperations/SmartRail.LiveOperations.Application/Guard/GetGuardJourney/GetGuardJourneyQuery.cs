using SmartRail.LiveOperations.Application.ControllerView;
using SmartRail.LiveOperations.Application.LiveJourneys;
using SmartRail.LiveOperations.Domain.LiveState;
using SmartRail.RailNetwork.Contracts;
using SmartRail.SharedKernel.Application.Abstractions;
using SmartRail.SharedKernel.Application.Messaging;
using SmartRail.SharedKernel.Results;

namespace SmartRail.LiveOperations.Application.Guard.GetGuardJourney;

/// <summary>
/// One journey with its calling stops and the changes published on it, for the guard it is assigned
/// to (guard-assignments spec, GRD-08).
/// </summary>
/// <param name="JourneyId">
/// The journey to read. A journey assigned to another guard is reported as not found, so the ID
/// reveals nothing, and a journey of any service date is served because a queued sync may arrive
/// after midnight.
/// </param>
public sealed record GetGuardJourneyQuery(Guid JourneyId) : IQuery<GuardJourneyDetailResponse>;

internal sealed class GetGuardJourneyQueryHandler(
    ILiveJourneyRepository journeys,
    IStatusChangeReader statusChanges,
    ITimetable timetable,
    INetworkCatalog network,
    ICurrentUser currentUser)
    : IQueryHandler<GetGuardJourneyQuery, GuardJourneyDetailResponse>
{
    public async Task<Result<GuardJourneyDetailResponse>> Handle(
        GetGuardJourneyQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var state = await journeys.GetAsync(query.JourneyId, cancellationToken);
        if (state is null || state.GuardStaffId != currentUser.StaffId)
        {
            // Another guard's journey answers exactly as an unknown one does, so a guard cannot
            // probe for the existence of journeys they are not assigned to (design D1).
            return Result.Failure<GuardJourneyDetailResponse>(
                JourneyLiveStateErrors.NotFound(query.JourneyId));
        }

        // The journey's own service date, not today: a guard's queue syncs after midnight.
        var journey = (await timetable.GetScheduledJourneysAsync(state.ServiceDate, cancellationToken))
            .FirstOrDefault(candidate => candidate.Id == state.Id);
        var stations = new StationIndex(await network.GetStationsAsync(cancellationToken));
        var changes = await statusChanges.GetForJourneyAsync(state.Id, cancellationToken);

        var detail = GuardJourneyMapping.ToDetail(state, journey, stations, changes);

        return Result.Success(detail);
    }
}
