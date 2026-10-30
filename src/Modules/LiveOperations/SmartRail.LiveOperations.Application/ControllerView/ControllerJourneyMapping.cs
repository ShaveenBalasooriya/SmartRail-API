using SmartRail.LiveOperations.Application.LiveState;
using SmartRail.LiveOperations.Domain.LiveState;
using SmartRail.RailNetwork.Contracts;

namespace SmartRail.LiveOperations.Application.ControllerView;

/// <summary>
/// Looks up the names the controller's live view shows for the identifiers the live state holds:
/// stations and their platforms come from the Rail Network catalogue, read once per request.
/// </summary>
internal sealed class StationIndex
{
    private readonly Dictionary<Guid, StationDto> _stations;
    private readonly Dictionary<Guid, PlatformDto> _platforms;

    public StationIndex(IReadOnlyList<StationDto> stations)
    {
        ArgumentNullException.ThrowIfNull(stations);

        _stations = stations.ToDictionary(station => station.Id);
        _platforms = stations
            .SelectMany(station => station.Platforms)
            .ToDictionary(platform => platform.Id);
    }

    /// <summary>The station's reference, or <see langword="null"/> when there is no ID or the catalogue lacks it.</summary>
    public StationRef? Station(Guid? stationId) =>
        stationId is { } id && _stations.TryGetValue(id, out var station)
            ? new StationRef(station.Id, station.Name)
            : null;

    /// <summary>The platform's reference, or <see langword="null"/> when there is no ID or the catalogue lacks it.</summary>
    public PlatformRef? Platform(Guid? platformId) =>
        platformId is { } id && _platforms.TryGetValue(id, out var platform)
            ? new PlatformRef(platform.Id, platform.Number)
            : null;
}

/// <summary>
/// Maps the live state and the names around it to the controller's responses. The journey's names
/// come from the timetable; when the timetable does not hold the journey they are left empty rather
/// than failing the view.
/// </summary>
internal static class ControllerJourneyMapping
{
    public static ControllerJourneyResponse ToSummary(
        JourneyLiveState state,
        JourneyDto? journey,
        StationIndex stations,
        IReadOnlyDictionary<Guid, string> staffNames,
        bool isStale)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(stations);
        ArgumentNullException.ThrowIfNull(staffNames);

        return new ControllerJourneyResponse(
            state.Id,
            journey?.ServiceName,
            journey?.LineCode,
            journey?.LineName,
            journey?.Direction,
            LiveStateMapping.ToContract(state.Status),
            state.DelayMinutes,
            stations.Station(state.LastDepartedStationId),
            stations.Station(state.NextStationId),
            NameOf(staffNames, state.GuardStaffId),
            state.LastUpdatedAt,
            isStale);
    }

    public static ControllerJourneyDetailResponse ToDetail(
        JourneyLiveState state,
        JourneyDto? journey,
        StationIndex stations,
        IReadOnlyDictionary<Guid, string> staffNames,
        IReadOnlyList<StatusChangeView> changes,
        bool isStale)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(stations);
        ArgumentNullException.ThrowIfNull(staffNames);
        ArgumentNullException.ThrowIfNull(changes);

        var stops = state.Stops
            .OrderBy(stop => stop.Sequence)
            .Select(stop => new ControllerStopResponse(
                stop.Sequence,
                stations.Station(stop.StationId),
                stop.IsCalling,
                stop.ScheduledArrival,
                stop.ScheduledDeparture,
                stop.ExpectedArrival,
                stop.ExpectedDeparture,
                stop.ActualDeparture,
                stations.Platform(stop.PlatformId)))
            .ToArray();

        var history = changes
            .Select(change => ToChange(change, staffNames))
            .ToArray();

        return new ControllerJourneyDetailResponse(
            ToSummary(state, journey, stations, staffNames, isStale),
            stops,
            history);
    }

    /// <summary>
    /// One published change as the history and the publish response both show it: the same shape, so
    /// a controller sees a newly published change exactly as it will appear in the journey's record.
    /// </summary>
    public static ControllerChangeResponse ToChange(
        StatusChangeView change,
        IReadOnlyDictionary<Guid, string> staffNames)
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(staffNames);

        return new ControllerChangeResponse(
            change.Id,
            change.Type,
            change.Values,
            NameOf(staffNames, change.PublisherStaffId),
            change.PublishedAt,
            change.Provenance,
            change.Corrected);
    }

    private static string? NameOf(IReadOnlyDictionary<Guid, string> staffNames, Guid staffId) =>
        staffNames.TryGetValue(staffId, out var name) ? name : null;
}
