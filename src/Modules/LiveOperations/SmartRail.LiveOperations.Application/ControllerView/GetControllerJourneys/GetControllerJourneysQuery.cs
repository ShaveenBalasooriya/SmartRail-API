using SmartRail.IdentityAccess.Contracts;
using SmartRail.LiveOperations.Application.Abstractions;
using SmartRail.LiveOperations.Application.LiveJourneys;
using SmartRail.RailNetwork.Contracts;
using SmartRail.SharedKernel.Application.Messaging;
using SmartRail.SharedKernel.Results;

namespace SmartRail.LiveOperations.Application.ControllerView.GetControllerJourneys;

/// <summary>Today's journeys in the railway's time zone, ordered by scheduled departure (NC-01, NC-02, NC-07).</summary>
public sealed record GetControllerJourneysQuery : IQuery<IReadOnlyList<ControllerJourneyResponse>>;

internal sealed class GetControllerJourneysQueryHandler(
    ILiveJourneyRepository journeys,
    ITimetable timetable,
    INetworkCatalog network,
    IStaffDirectory staff,
    IRailwayCalendar calendar,
    OperationsOptions options,
    TimeProvider timeProvider)
    : IQueryHandler<GetControllerJourneysQuery, IReadOnlyList<ControllerJourneyResponse>>
{
    public async Task<Result<IReadOnlyList<ControllerJourneyResponse>>> Handle(
        GetControllerJourneysQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var now = timeProvider.GetUtcNow();
        var today = calendar.DayOf(now);
        var threshold = TimeSpan.FromMinutes(options.StaleAfterMinutes);

        var states = await journeys.GetByDateAsync(today, cancellationToken);
        var scheduled = (await timetable.GetScheduledJourneysAsync(today, cancellationToken))
            .ToDictionary(journey => journey.Id);
        var stations = new StationIndex(await network.GetStationsAsync(cancellationToken));
        var staffNames = await staff.GetDisplayNamesAsync(
            states.Select(state => state.GuardStaffId).Distinct().ToArray(),
            cancellationToken);

        var summaries = states
            .Select(state => ControllerJourneyMapping.ToSummary(
                state,
                scheduled.GetValueOrDefault(state.Id),
                stations,
                staffNames,
                state.IsStaleAt(now, threshold)))
            .ToArray();

        return Result.Success<IReadOnlyList<ControllerJourneyResponse>>(summaries);
    }
}
