using SmartRail.LiveOperations.Contracts;
using SmartRail.RailNetwork.Contracts;

namespace SmartRail.Catering.UnitTests.TestDoubles;

/// <summary>Fresh contract objects for each test; no database or shared journey state.</summary>
internal static class TestData
{
    internal static readonly DateTimeOffset Now = new(2026, 10, 30, 6, 0, 0, TimeSpan.Zero);

    internal static StationDto Station(
        Guid id,
        Guid platformId,
        Guid lineId,
        string name = "Colombo Fort") =>
        new(
            id,
            "CMB",
            name,
            true,
            [new PlatformDto(platformId, id, "1")],
            [new StationLineDto(lineId, "MAIN", "#005bbb", "Main Line")]);

    internal static LineDto Line(Guid id) => new(id, "MAIN", "#005bbb", "Main Line", []);

    internal static ServiceDto Service(Guid id, Guid lineId) =>
        new(id, "Coastal Express", lineId, ServiceDirection.Outbound, Guid.CreateVersion7(), []);

    internal static LiveJourneyView Journey(
        Guid journeyId,
        Guid serviceId,
        Guid lineId,
        IReadOnlyList<LiveStopView> stops,
        DateOnly? serviceDate = null,
        int? delayMinutes = null) =>
        new(
            journeyId,
            serviceId,
            lineId,
            serviceDate ?? DateOnly.FromDateTime(Now.DateTime),
            JourneyLifecycleStatus.Running,
            delayMinutes,
            null,
            null,
            Now,
            7,
            Guid.CreateVersion7(),
            stops,
            null);

    internal static LiveStopView Stop(
        int sequence,
        Guid stationId,
        DateTimeOffset scheduledArrival,
        Guid? platformId = null,
        bool isCalling = true,
        DateTimeOffset? expectedArrival = null,
        DateTimeOffset? actualDeparture = null,
        DateTimeOffset? platformChangedAt = null) =>
        new(
            sequence,
            stationId,
            isCalling,
            scheduledArrival,
            scheduledArrival.AddMinutes(2),
            expectedArrival,
            expectedArrival?.AddMinutes(2),
            actualDeparture,
            platformId,
            platformChangedAt);

    internal static JourneyEventView Event(
        JourneyEventType type,
        LiveJourneyView snapshot,
        Guid? correctsEventId = null) =>
        new(
            Guid.CreateVersion7(),
            type,
            snapshot.JourneyId,
            8,
            42,
            Guid.CreateVersion7(),
            Now,
            Now,
            snapshot,
            correctsEventId is null ? null : Guid.CreateVersion7(),
            correctsEventId);
}
