using SmartRail.LiveOperations.Domain.Calculation;
using SmartRail.RailNetwork.Contracts;

namespace SmartRail.LiveOperations.Application.LiveState;

/// <summary>
/// Builds the calculator's <see cref="Baseline"/> from whichever source supplies a journey: the
/// Rail Network announcement or the timetable read the backfill uses. Both carry the same stop
/// shape, so the created live state is the same either way.
/// </summary>
internal static class LiveStateBaseline
{
    public static Baseline From(JourneyGenerated journey, DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(journey);

        return new Baseline(
            journey.JourneyId,
            journey.ServiceDate,
            journey.GuardStaffId,
            Stops(journey.Stops),
            createdAt);
    }

    public static Baseline From(JourneyDto journey, DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(journey);

        return new Baseline(
            journey.Id,
            journey.ServiceDate,
            journey.GuardStaffId,
            Stops(journey.Stops),
            createdAt);
    }

    private static BaselineStop[] Stops(IReadOnlyList<JourneyStopDto> stops) => [.. stops
        .OrderBy(stop => stop.Sequence)
        .Select(stop => new BaselineStop(
            stop.Sequence,
            stop.StationId,
            stop.IsCalling,
            stop.ScheduledArrival,
            stop.ScheduledDeparture,
            stop.PlatformId))];
}
