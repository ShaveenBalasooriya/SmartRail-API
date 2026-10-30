using SmartRail.Distribution.Application.Public;
using SmartRail.LiveOperations.Contracts;
using SmartRail.RailNetwork.Contracts;

namespace SmartRail.Distribution.UnitTests.Public;

/// <summary>
/// The live state and the names the guidance tests build from: a three-stop journey from Alpha to
/// Gamma on one line, a second station group to run a different destination, and the railway's
/// clock at UTC+05:30, the offset the default railway time zone has.
/// </summary>
internal static class GuidanceFixture
{
    public const string LineName = "Main Line";
    public const string ServiceName = "Kandy Express";
    public const string OtherServiceName = "Coastal Express";

    public static readonly DateOnly ServiceDate = new(2026, 10, 28);

    /// <summary>The railway's clock, at the UTC+05:30 offset <c>Asia/Colombo</c> uses.</summary>
    public static readonly TimeZoneInfo RailwayZone = TimeZoneInfo.CreateCustomTimeZone(
        "SmartRail-Railway",
        TimeSpan.FromMinutes(330),
        "Railway",
        "Railway");

    public static readonly Guid LineId = Guid.CreateVersion7();
    public static readonly Guid ServiceId = Guid.CreateVersion7();
    public static readonly Guid OtherServiceId = Guid.CreateVersion7();
    public static readonly Guid GuardStaffId = Guid.CreateVersion7();

    public static readonly Guid Alpha = Guid.CreateVersion7();
    public static readonly Guid Beta = Guid.CreateVersion7();
    public static readonly Guid Gamma = Guid.CreateVersion7();
    public static readonly Guid Delta = Guid.CreateVersion7();

    public static readonly Guid AlphaPlatformOne = Guid.CreateVersion7();
    public static readonly Guid AlphaPlatformTwo = Guid.CreateVersion7();
    public static readonly Guid GammaPlatformOne = Guid.CreateVersion7();

    public static NetworkLookup Lookup() =>
        new(
            new Dictionary<Guid, StationDto>
            {
                [Alpha] = new(Alpha, "ALP", "Alpha", IsInterchange: false,
                    [new PlatformDto(AlphaPlatformOne, Alpha, "1"), new PlatformDto(AlphaPlatformTwo, Alpha, "2")],
                    []),
                [Beta] = new(Beta, "BET", "Beta", IsInterchange: false, [], []),
                [Gamma] = new(Gamma, "GAM", "Gamma", IsInterchange: false,
                    [new PlatformDto(GammaPlatformOne, Gamma, "1")],
                    []),
                [Delta] = new(Delta, "DEL", "Delta", IsInterchange: false, [], []),
            },
            new Dictionary<Guid, PlatformDto>
            {
                [AlphaPlatformOne] = new(AlphaPlatformOne, Alpha, "1"),
                [AlphaPlatformTwo] = new(AlphaPlatformTwo, Alpha, "2"),
                [GammaPlatformOne] = new(GammaPlatformOne, Gamma, "1"),
            },
            new Dictionary<Guid, LineDto>
            {
                [LineId] = new(LineId, "L1", "#1d4ed8", LineName, []),
            },
            new Dictionary<Guid, ServiceDto>
            {
                [ServiceId] = new(ServiceId, ServiceName, LineId, ServiceDirection.Outbound, GuardStaffId, []),
                [OtherServiceId] = new(OtherServiceId, OtherServiceName, LineId, ServiceDirection.Outbound, GuardStaffId, []),
            });

    /// <summary>One journey from Alpha to Gamma, scheduled to leave at 08:00.</summary>
    public static LiveJourneyView Journey(
        JourneyLifecycleStatus status = JourneyLifecycleStatus.Scheduled,
        int? delayMinutes = 0,
        IReadOnlyList<LiveStopView>? stops = null,
        DateTimeOffset? correctedAt = null,
        Guid? journeyId = null,
        Guid? serviceId = null) =>
        new(
            journeyId ?? Guid.CreateVersion7(),
            serviceId ?? ServiceId,
            LineId,
            ServiceDate,
            status,
            delayMinutes,
            LastDepartedStationId: null,
            NextStationId: Gamma,
            At(7, 30),
            LastSequenceNumber: 0,
            GuardStaffId,
            stops ?? Route(At(8, 0), At(8, 2)),
            correctedAt);

    /// <summary>The three calling stops of the fixture route, from Alpha to Gamma or another destination.</summary>
    public static IReadOnlyList<LiveStopView> Route(
        DateTimeOffset scheduledDeparture,
        DateTimeOffset expectedDeparture,
        Guid? originPlatform = null,
        Guid? destination = null) =>
    [
        new(1, Alpha, IsCalling: true, ScheduledArrival: null, ScheduledDeparture: scheduledDeparture,
            ExpectedArrival: null, ExpectedDeparture: expectedDeparture, ActualDeparture: null,
            PlatformId: originPlatform ?? AlphaPlatformOne, PlatformChangedAt: null),
        new(2, Beta, IsCalling: true, ScheduledArrival: null, ScheduledDeparture: scheduledDeparture.AddMinutes(10),
            ExpectedArrival: null, ExpectedDeparture: expectedDeparture.AddMinutes(10), ActualDeparture: null,
            PlatformId: null, PlatformChangedAt: null),
        new(3, destination ?? Gamma, IsCalling: true, ScheduledArrival: scheduledDeparture.AddMinutes(20),
            ScheduledDeparture: null, ExpectedArrival: expectedDeparture.AddMinutes(20), ExpectedDeparture: null,
            ActualDeparture: null, PlatformId: null, PlatformChangedAt: null),
    ];

    /// <summary>A cancelled journey's stops: the timetable without any expected times.</summary>
    public static LiveStopView[] CancelledStops() => CancelledStops(At(8, 0));

    /// <summary>
    /// A journey ended early at Beta: Alpha has departed, Beta still has expected times and the stop
    /// after the last one served has none.
    /// </summary>
    public static IReadOnlyList<LiveStopView> TerminatedStops() =>
    [
        new(1, Alpha, IsCalling: true, ScheduledArrival: null, ScheduledDeparture: At(8, 0),
            ExpectedArrival: null, ExpectedDeparture: At(8, 0), ActualDeparture: At(8, 0),
            PlatformId: AlphaPlatformOne, PlatformChangedAt: null),
        new(2, Beta, IsCalling: true, ScheduledArrival: null, ScheduledDeparture: At(8, 10),
            ExpectedArrival: null, ExpectedDeparture: At(8, 12), ActualDeparture: null,
            PlatformId: null, PlatformChangedAt: null),
        new(3, Gamma, IsCalling: true, ScheduledArrival: At(8, 20), ScheduledDeparture: null,
            ExpectedArrival: null, ExpectedDeparture: null, ActualDeparture: null,
            PlatformId: null, PlatformChangedAt: null),
    ];

    /// <summary>A later journey that runs the same route, which guidance may name as the next service.</summary>
    public static LiveJourneyView Candidate(
        DateTimeOffset scheduledDeparture,
        DateTimeOffset expectedDeparture,
        JourneyLifecycleStatus status = JourneyLifecycleStatus.Scheduled,
        Guid? destination = null,
        Guid? originPlatform = null,
        Guid? journeyId = null,
        Guid? serviceId = null) =>
        Journey(
            status,
            delayMinutes: 0,
            Route(scheduledDeparture, expectedDeparture, originPlatform, destination),
            correctedAt: null,
            journeyId,
            serviceId ?? OtherServiceId);

    /// <summary>A stop's platform changed to <paramref name="platformId"/> at the time the change became known.</summary>
    public static LiveStopView WithPlatformChange(
        LiveStopView stop,
        Guid platformId,
        DateTimeOffset changedAt) =>
        stop with { PlatformId = platformId, PlatformChangedAt = changedAt };

    private static LiveStopView[] CancelledStops(DateTimeOffset departure) =>
        Route(departure, departure)
            .Select(stop => stop with { ExpectedArrival = null, ExpectedDeparture = null })
            .ToArray();

    /// <summary>A time of day on the railway's clock, at the UTC+05:30 offset.</summary>
    public static DateTimeOffset At(int hour, int minute) =>
        new(2026, 10, 28, hour, minute, 0, TimeSpan.FromMinutes(330));
}
