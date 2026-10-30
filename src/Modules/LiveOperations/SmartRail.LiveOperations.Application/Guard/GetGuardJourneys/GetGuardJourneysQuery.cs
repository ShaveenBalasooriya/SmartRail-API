using SmartRail.LiveOperations.Application.Abstractions;
using SmartRail.LiveOperations.Application.ControllerView;
using SmartRail.LiveOperations.Application.LiveJourneys;
using SmartRail.RailNetwork.Contracts;
using SmartRail.SharedKernel.Application.Abstractions;
using SmartRail.SharedKernel.Application.Messaging;
using SmartRail.SharedKernel.Results;

namespace SmartRail.LiveOperations.Application.Guard.GetGuardJourneys;

/// <summary>
/// Today's journeys in the railway's time zone that are assigned to the calling guard, ordered by
/// scheduled departure (guard-assignments spec, GRD-01).
/// </summary>
public sealed record GetGuardJourneysQuery : IQuery<IReadOnlyList<GuardJourneyResponse>>;

internal sealed class GetGuardJourneysQueryHandler(
    ILiveJourneyRepository journeys,
    ITimetable timetable,
    INetworkCatalog network,
    IRailwayCalendar calendar,
    ICurrentUser currentUser,
    TimeProvider timeProvider)
    : IQueryHandler<GetGuardJourneysQuery, IReadOnlyList<GuardJourneyResponse>>
{
    public async Task<Result<IReadOnlyList<GuardJourneyResponse>>> Handle(
        GetGuardJourneysQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // The staff policy requires a staff id; a caller without one is assigned nothing.
        if (currentUser.StaffId is not { } staffId)
        {
            return Result.Success<IReadOnlyList<GuardJourneyResponse>>([]);
        }

        var today = calendar.DayOf(timeProvider.GetUtcNow());
        var states = await journeys.GetByDateAsync(today, cancellationToken);
        var scheduled = (await timetable.GetScheduledJourneysAsync(today, cancellationToken))
            .ToDictionary(journey => journey.Id);
        var stations = new StationIndex(await network.GetStationsAsync(cancellationToken));

        // The repository already orders by scheduled departure, so filtering keeps that order.
        var own = states
            .Where(state => state.GuardStaffId == staffId)
            .Select(state => GuardJourneyMapping.ToResponse(
                state,
                scheduled.GetValueOrDefault(state.Id),
                stations))
            .ToArray();

        return Result.Success<IReadOnlyList<GuardJourneyResponse>>(own);
    }
}
