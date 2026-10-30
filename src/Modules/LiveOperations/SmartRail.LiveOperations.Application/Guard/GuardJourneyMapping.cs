using SmartRail.LiveOperations.Application.ControllerView;
using SmartRail.LiveOperations.Application.LiveState;
using SmartRail.LiveOperations.Domain.LiveState;
using SmartRail.RailNetwork.Contracts;

namespace SmartRail.LiveOperations.Application.Guard;

/// <summary>
/// Maps the live state and the names around it to the guard's responses. The station, platform and
/// change names are the ones the controller's live view already resolves, and the journey's names
/// come from the timetable; when the timetable does not hold the journey they are left empty rather
/// than failing the view.
/// </summary>
internal static class GuardJourneyMapping
{
    public static GuardJourneyResponse ToResponse(
        JourneyLiveState state,
        JourneyDto? journey,
        StationIndex stations)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(stations);

        var callingStops = state.Stops
            .Where(stop => stop.IsCalling)
            .OrderBy(stop => stop.Sequence)
            .ToArray();
        var finalSequence = callingStops.Length == 0 ? (int?)null : callingStops[^1].Sequence;

        var stops = callingStops
            .Select(stop => new GuardStopResponse(
                stop.Sequence,
                stations.Station(stop.StationId),
                stop.ScheduledArrival,
                stop.ScheduledDeparture,
                stop.ExpectedArrival,
                stop.ExpectedDeparture,
                stop.ActualDeparture,
                stations.Platform(stop.PlatformId),
                stop.PlatformChangedAt,
                stop.ActualDeparture is not null,
                stop.Sequence == finalSequence))
            .ToArray();

        return new GuardJourneyResponse(
            state.Id,
            journey?.ServiceName,
            journey?.LineCode,
            journey?.LineName,
            journey?.Direction,
            LiveStateMapping.ToContract(state.Status),
            state.DelayMinutes,
            state.LastUpdatedAt,
            stops);
    }

    public static GuardJourneyDetailResponse ToDetail(
        JourneyLiveState state,
        JourneyDto? journey,
        StationIndex stations,
        IReadOnlyList<StatusChangeView> changes)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(stations);
        ArgumentNullException.ThrowIfNull(changes);

        var published = changes
            .Select(change => new GuardChangeResponse(
                change.Id,
                change.Type,
                change.Values,
                change.PublishedAt,
                change.Corrected))
            .ToArray();

        return new GuardJourneyDetailResponse(ToResponse(state, journey, stations), published);
    }
}
