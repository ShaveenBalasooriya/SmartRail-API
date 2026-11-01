using SmartRail.LiveOperations.Domain.Calculation;
using SmartRail.LiveOperations.Domain.Changes;
using SmartRail.LiveOperations.Domain.Departures;
using SmartRail.LiveOperations.Domain.LiveState;
using SmartRail.RailNetwork.Contracts;

namespace SmartRail.LiveOperations.UnitTests.TestDoubles;

/// <summary>Small builders for valid domain objects; every call returns fresh test data.</summary>
internal static class TestData
{
    internal static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    internal static JourneyLiveState Journey(
        Guid? journeyId = null,
        Guid? guardStaffId = null,
        DateOnly? serviceDate = null,
        Guid? stationId = null,
        Guid? platformId = null)
    {
        var firstStationId = stationId ?? Guid.CreateVersion7();
        var baseline = new Baseline(
            journeyId ?? Guid.CreateVersion7(),
            serviceDate ?? DateOnly.FromDateTime(Now.DateTime),
            guardStaffId ?? Guid.CreateVersion7(),
            [
                new BaselineStop(
                    1,
                    firstStationId,
                    true,
                    null,
                    Now.AddHours(-2),
                    platformId ?? Guid.CreateVersion7()),
                new BaselineStop(
                    2,
                    Guid.CreateVersion7(),
                    true,
                    Now.AddHours(-1),
                    Now.AddHours(-1).AddMinutes(5),
                    Guid.CreateVersion7()),
                new BaselineStop(
                    3,
                    Guid.CreateVersion7(),
                    true,
                    Now.AddHours(1),
                    null,
                    Guid.CreateVersion7()),
            ],
            Now.AddHours(-3));

        return JourneyLiveState.Create(
                baseline,
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Now.AddHours(-3))
            .Value;
    }

    internal static JourneyLiveState RunningJourney(
        Guid? journeyId = null,
        Guid? guardStaffId = null,
        DateOnly? serviceDate = null,
        Guid? stationId = null,
        Guid? platformId = null)
    {
        var state = Journey(journeyId, guardStaffId, serviceDate, stationId, platformId);
        state.Apply(
            [new DepartureFact(1, Now.AddHours(-2), Now.AddHours(-2).AddMinutes(1), 1)],
            Now.AddHours(-2).AddMinutes(1));
        return state;
    }

    internal static JourneyLiveState CancelledJourney(
        Guid? journeyId = null,
        Guid? guardStaffId = null,
        DateOnly? serviceDate = null)
    {
        var state = Journey(journeyId, guardStaffId, serviceDate);
        state.Apply(
            [new ChangeFact(ChangeKind.Cancellation, null, null, null, Now.AddMinutes(-1), 1, false)],
            Now.AddMinutes(-1));
        return state;
    }

    internal static JourneyLiveState TerminatedJourney(
        Guid? journeyId = null,
        Guid? guardStaffId = null,
        DateOnly? serviceDate = null)
    {
        var state = RunningJourney(journeyId, guardStaffId, serviceDate);
        state.Apply(
            [new DepartureFact(1, Now.AddHours(-2), Now.AddHours(-2).AddMinutes(1), 1),
                new ChangeFact(ChangeKind.Termination, 1, null, null, Now.AddMinutes(-1), 2, false)],
            Now.AddMinutes(-1));
        return state;
    }

    internal static JourneyDto TimetableJourney(JourneyLiveState state) => new(
        state.Id,
        state.ServiceId,
        "Coastal Express",
        state.LineId,
        "COAST",
        "Coastal Line",
        ServiceDirection.Outbound,
        state.ServiceDate,
        state.GuardStaffId,
        state.Stops.Min(stop => stop.ScheduledDeparture ?? stop.ScheduledArrival) ?? Now,
        [.. state.Stops
            .OrderBy(stop => stop.Sequence)
            .Select(stop => new JourneyStopDto(
                stop.Sequence,
                stop.StationId,
                stop.IsCalling,
                stop.ScheduledArrival,
                stop.ScheduledDeparture,
                stop.PlatformId))]);

    internal static DepartureLog Departure(
        Guid? id = null,
        Guid? journeyId = null,
        Guid? guardStaffId = null,
        int stopSequence = 1,
        DateTimeOffset? departedAt = null,
        DateTimeOffset? receivedAt = null,
        DelayCause? delayCause = null) =>
        DepartureLog.Create(
                id ?? Guid.CreateVersion7(),
                journeyId ?? Guid.CreateVersion7(),
                stopSequence,
                Guid.CreateVersion7(),
                guardStaffId ?? Guid.CreateVersion7(),
                departedAt ?? Now.AddMinutes(-10),
                delayCause,
                receivedAt ?? Now)
            .Value;

    internal static StatusChange Delay(
        Guid journeyId,
        Guid publishedBy,
        int stopSequence = 1,
        int minutes = 10,
        DateTimeOffset? publishedAt = null) =>
        StatusChange.PublishDelay(
                journeyId,
                stopSequence,
                minutes,
                publishedBy,
                publishedAt ?? Now)
            .Value;

    internal static StatusChange Cancellation(
        Guid journeyId,
        Guid publishedBy,
        DateTimeOffset? publishedAt = null) =>
        StatusChange.PublishCancellation(journeyId, publishedBy, publishedAt ?? Now).Value;

    internal static StatusChange Termination(
        Guid journeyId,
        Guid publishedBy,
        int lastStopSequence = 1,
        DateTimeOffset? publishedAt = null) =>
        StatusChange.PublishTermination(
                journeyId,
                lastStopSequence,
                publishedBy,
                publishedAt ?? Now)
            .Value;

    internal static StatusChange PlatformChange(
        Guid journeyId,
        Guid publishedBy,
        int stopSequence = 1,
        Guid? newPlatformId = null,
        Guid? previousPlatformId = null,
        DateTimeOffset? publishedAt = null) =>
        StatusChange.PublishPlatformChange(
                journeyId,
                stopSequence,
                newPlatformId ?? Guid.CreateVersion7(),
                previousPlatformId ?? Guid.CreateVersion7(),
                publishedBy,
                publishedAt ?? Now)
            .Value;

    internal static StatusChange Correction(
        Guid journeyId,
        Guid correctsChangeId,
        Guid publishedBy,
        DateTimeOffset? publishedAt = null) =>
        StatusChange.PublishCorrection(
                journeyId,
                correctsChangeId,
                publishedBy,
                publishedAt ?? Now)
            .Value;
}
