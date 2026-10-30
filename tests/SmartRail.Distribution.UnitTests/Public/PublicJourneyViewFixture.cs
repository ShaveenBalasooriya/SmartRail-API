using SmartRail.Distribution.Application.Public;
using SmartRail.LiveOperations.Contracts;
using SmartRail.RailNetwork.Contracts;

namespace SmartRail.Distribution.UnitTests.Public;

/// <summary>
/// The live state and the names the public view tests build from: a three-stop journey whose middle
/// stop is passed without calling, on a line with two platforms at its first station.
/// </summary>
internal static class PublicJourneyViewFixture
{
    public const string ServiceName = "Kandy Express";

    public static readonly DateOnly ServiceDate = new(2026, 10, 28);

    public static readonly Guid ServiceId = Guid.CreateVersion7();
    public static readonly Guid LineId = Guid.CreateVersion7();
    public static readonly Guid GuardStaffId = Guid.CreateVersion7();

    public static readonly Guid Alpha = Guid.CreateVersion7();
    public static readonly Guid Beta = Guid.CreateVersion7();
    public static readonly Guid Gamma = Guid.CreateVersion7();

    public static readonly Guid AlphaPlatformOne = Guid.CreateVersion7();
    public static readonly Guid AlphaPlatformTwo = Guid.CreateVersion7();

    public static NetworkLookup Lookup() =>
        new(
            new Dictionary<Guid, StationDto>
            {
                [Alpha] = new(Alpha, "ALP", "Alpha", IsInterchange: false,
                    [new PlatformDto(AlphaPlatformOne, Alpha, "1"), new PlatformDto(AlphaPlatformTwo, Alpha, "2")],
                    []),
                [Beta] = new(Beta, "BET", "Beta", IsInterchange: false, [], []),
                [Gamma] = new(Gamma, "GAM", "Gamma", IsInterchange: false, [], []),
            },
            new Dictionary<Guid, PlatformDto>
            {
                [AlphaPlatformOne] = new(AlphaPlatformOne, Alpha, "1"),
                [AlphaPlatformTwo] = new(AlphaPlatformTwo, Alpha, "2"),
            },
            new Dictionary<Guid, LineDto>
            {
                [LineId] = new(LineId, "L1", "#1d4ed8", "Main Line", []),
            },
            new Dictionary<Guid, ServiceDto>
            {
                [ServiceId] = new(ServiceId, ServiceName, LineId, ServiceDirection.Outbound, GuardStaffId, []),
            });

    public static LiveJourneyView Journey(
        JourneyLifecycleStatus status = JourneyLifecycleStatus.Scheduled,
        IReadOnlyList<LiveStopView>? stops = null,
        DateTimeOffset? correctedAt = null,
        Guid? serviceId = null,
        Guid? lineId = null,
        Guid? guardStaffId = null,
        Guid? journeyId = null) =>
        new(
            journeyId ?? Guid.CreateVersion7(),
            serviceId ?? ServiceId,
            lineId ?? LineId,
            ServiceDate,
            status,
            DelayMinutes: 2,
            LastDepartedStationId: null,
            NextStationId: Gamma,
            LastUpdatedAt: At(7, 30),
            LastSequenceNumber: 0,
            guardStaffId ?? GuardStaffId,
            stops ?? Stops(),
            correctedAt);

    /// <summary>
    /// The journey's default stops: one calling stop with a platform, one passed without calling,
    /// and a terminus with no platform.
    /// </summary>
    public static IReadOnlyList<LiveStopView> Stops(
        DateTimeOffset? firstPlatformChangedAt = null,
        Guid? firstPlatformId = null,
        DateTimeOffset? scheduledDeparture = null,
        DateTimeOffset? expectedDeparture = null) =>
    [
        new(1, Alpha, IsCalling: true, ScheduledArrival: null, ScheduledDeparture: scheduledDeparture ?? At(8, 0),
            ExpectedArrival: null, ExpectedDeparture: expectedDeparture ?? At(8, 2), ActualDeparture: null,
            PlatformId: firstPlatformId ?? AlphaPlatformOne, PlatformChangedAt: firstPlatformChangedAt),
        new(2, Beta, IsCalling: false, ScheduledArrival: null, ScheduledDeparture: null,
            ExpectedArrival: null, ExpectedDeparture: null, ActualDeparture: null,
            PlatformId: null, PlatformChangedAt: null),
        new(3, Gamma, IsCalling: true, ScheduledArrival: At(8, 20), ScheduledDeparture: null,
            ExpectedArrival: At(8, 22), ExpectedDeparture: null, ActualDeparture: null,
            PlatformId: null, PlatformChangedAt: null),
    ];

    public static DateTimeOffset At(int hour, int minute) =>
        new(2026, 10, 28, hour, minute, 0, TimeSpan.Zero);

    /// <summary>The public view of <see cref="Journey"/>, which is what the departure selection reads.</summary>
    public static PublicJourneyView View(
        JourneyLifecycleStatus status = JourneyLifecycleStatus.Scheduled,
        IReadOnlyList<LiveStopView>? stops = null,
        Guid? journeyId = null) =>
        PublicJourneyViewBuilder.Build(Journey(status, stops, journeyId: journeyId), Lookup());
}


