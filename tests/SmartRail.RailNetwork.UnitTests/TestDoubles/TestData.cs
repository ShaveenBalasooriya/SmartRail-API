using SmartRail.RailNetwork.Contracts;
using SmartRail.RailNetwork.Domain.Entities;
using ContractDirection = SmartRail.RailNetwork.Contracts.ServiceDirection;
using ServiceDirection = SmartRail.RailNetwork.Domain.Entities.ServiceDirection;

namespace SmartRail.RailNetwork.UnitTests.TestDoubles;

/// <summary>
/// Small builders for valid network data; every call returns fresh identifiers, so no test can see
/// another test's data.
/// </summary>
internal static class TestData
{
    /// <summary>The railway calendar day every test builds its journeys on.</summary>
    internal static readonly DateOnly ServiceDate = new(2026, 11, 1);

    /// <summary>The frozen instant: 07:30 on the service date in the railway's zone (UTC+05:30).</summary>
    internal static readonly DateTimeOffset Now = new(2026, 11, 1, 2, 0, 0, TimeSpan.Zero);

    /// <summary>The first stop's local departure, which is a journey's scheduled departure.</summary>
    internal static readonly TimeOnly FirstDeparture = new(8, 0);

    /// <summary>The second stop's local arrival.</summary>
    internal static readonly TimeOnly SecondArrival = new(8, 30);

    /// <summary>A clock frozen at <see cref="Now" /> and <see cref="ServiceDate" />.</summary>
    internal static StubRailwayClock Clock() => new(ServiceDate, Now);

    /// <summary>A two-stop outbound service: one departure, then one arrival.</summary>
    internal static Service TwoStopService() =>
        ServiceWith(
            CallingStop(1, departure: FirstDeparture, arrival: null),
            CallingStop(2, arrival: SecondArrival, departure: null));

    /// <summary>A service carrying exactly the stops it was given, in the order it was given them.</summary>
    internal static Service ServiceWith(params ServiceStop[] stops) =>
        ServiceWith(ServiceDirection.Outbound, stops);

    /// <summary>A service in the given direction carrying exactly the stops it was given.</summary>
    internal static Service ServiceWith(ServiceDirection direction, params ServiceStop[] stops)
    {
        var service = CreateService(direction);

        foreach (var stop in stops)
        {
            service.Stops.Add(stop);
        }

        return service;
    }

    /// <summary>A valid service with no stops yet.</summary>
    internal static Service CreateService(ServiceDirection direction = ServiceDirection.Outbound) =>
        Service.Create(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                direction,
                $"Coastal 0010 {direction}",
                Guid.CreateVersion7())
            .Value;

    /// <summary>A calling stop, which is what a journey's first departure is read from.</summary>
    internal static ServiceStop CallingStop(int sequence, TimeOnly? arrival, TimeOnly? departure) =>
        ServiceStop.Create(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                sequence,
                isCalling: true,
                arrival,
                departure,
                Guid.CreateVersion7())
            .Value;

    /// <summary>A line as the read side returns it, with the stations it was given in that order.</summary>
    internal static LineDto Line(params LineStationDto[] stations) =>
        new(Guid.CreateVersion7(), "COAST", "#1B6CA8", "Coastal Line", stations);

    /// <summary>One station's place in a line's order.</summary>
    internal static LineStationDto LineStation(Guid stationId, int sequence) =>
        new(stationId, $"S{sequence:00}", $"Station {sequence}", sequence);

    /// <summary>A station with the platforms and lines it was given.</summary>
    internal static StationDto Station(
        Guid? id = null,
        string code = "S01",
        string name = "Central",
        bool isInterchange = false,
        IReadOnlyList<PlatformDto>? platforms = null,
        IReadOnlyList<StationLineDto>? lines = null) =>
        new(
            id ?? Guid.CreateVersion7(),
            code,
            name,
            isInterchange,
            platforms ?? [],
            lines ?? []);

    /// <summary>A platform that belongs to the station it is given.</summary>
    internal static PlatformDto Platform(Guid stationId, string number = "1") =>
        new(Guid.CreateVersion7(), stationId, number);

    /// <summary>The line entry a station carries.</summary>
    internal static StationLineDto StationLine(LineDto line) =>
        new(line.Id, line.Code, line.Color, line.Name);

    /// <summary>A timetable service with the stops it was given.</summary>
    internal static ServiceDto ScheduledService(
        Guid? id = null,
        ContractDirection direction = ContractDirection.Outbound,
        params ServiceStopDto[] stops) =>
        new(
            id ?? Guid.CreateVersion7(),
            $"Coastal 0010 {direction}",
            Guid.CreateVersion7(),
            direction,
            Guid.CreateVersion7(),
            stops);

    /// <summary>A stop of a timetable service, with local times of day.</summary>
    internal static ServiceStopDto ScheduledStop(
        int sequence,
        TimeOnly? arrival = null,
        TimeOnly? departure = null) =>
        new(sequence, Guid.CreateVersion7(), true, arrival, departure, Guid.CreateVersion7());

    /// <summary>A dated journey as the timetable read side returns it.</summary>
    internal static JourneyDto ScheduledJourney(
        Guid? id = null,
        DateOnly? serviceDate = null,
        params JourneyStopDto[] stops) =>
        new(
            id ?? Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "Coastal 0010 Outbound",
            Guid.CreateVersion7(),
            "COAST",
            "Coastal Line",
            ContractDirection.Outbound,
            serviceDate ?? ServiceDate,
            Guid.CreateVersion7(),
            Now,
            stops);
}
