using SmartRail.Distribution.Application.Public;
using SmartRail.Distribution.Application.Public.Display;
using SmartRail.LiveOperations.Contracts;
using SmartRail.RailNetwork.Contracts;

namespace SmartRail.Distribution.UnitTests.Public;

/// <summary>
/// The station display's placement and notice rules (station-display-feed spec): the platform the
/// journey is assigned now decides where it is listed, the departures are in expected-departure
/// order, the count bounds the board, and a train that moved leaves its old platform and is
/// announced there with the platform it moved to until its scheduled departure has passed.
/// </summary>
public sealed class PublicPlatformSelectionTests
{
    private static readonly Guid Alpha = PublicJourneyViewFixture.Alpha;
    private static readonly Guid Beta = PublicJourneyViewFixture.Beta;
    private static readonly Guid Gamma = PublicJourneyViewFixture.Gamma;

    private static readonly Guid PlatformOne = PublicJourneyViewFixture.AlphaPlatformOne;
    private static readonly Guid PlatformTwo = PublicJourneyViewFixture.AlphaPlatformTwo;

    [Fact]
    public void The_platform_lists_the_trains_assigned_to_it_in_expected_departure_order()
    {
        var late = View(
            Guid.CreateVersion7(),
            [Stop(1, Alpha, PlatformOne, departure: At(8, 0), expectedDeparture: At(8, 15)), Stop(2, Gamma, arrival: At(8, 40))]);
        var onTime = View(
            Guid.CreateVersion7(),
            [Stop(1, Alpha, PlatformOne, departure: At(8, 10), expectedDeparture: At(8, 10)), Stop(2, Gamma, arrival: At(8, 40))]);
        var otherPlatform = View(
            Guid.CreateVersion7(),
            [Stop(1, Alpha, PlatformTwo, departure: At(8, 1), expectedDeparture: At(8, 1)), Stop(2, Gamma, arrival: At(8, 40))]);

        var feed = Feed([late, onTime, otherPlatform], PlatformOne);

        Assert.Equal(
            [onTime.JourneyId, late.JourneyId],
            feed.Departures.Select(departure => departure.Journey.JourneyId).ToArray());
        Assert.All(feed.Departures, departure => Assert.Equal("1", departure.Origin.Platform?.Number));
        Assert.Empty(feed.MovedNotices);
    }

    [Fact]
    public void The_count_bounds_the_board()
    {
        Assert.Equal(8, PublicPlatformSelection.DefaultCount);
        Assert.Equal(20, PublicPlatformSelection.MaximumCount);

        var journeys = Enumerable.Range(0, 3)
            .Select(index => View(
                Guid.CreateVersion7(),
                [
                    Stop(1, Alpha, PlatformOne, departure: At(8, index), expectedDeparture: At(8, index)),
                    Stop(2, Gamma, arrival: At(9, 0)),
                ]))
            .ToArray();

        var feed = Feed(journeys, PlatformOne, count: 2);

        Assert.Equal(2, feed.Departures.Count);
    }

    [Fact]
    public void A_replatformed_train_leaves_the_old_platform_and_is_announced_there()
    {
        var journeyId = Guid.CreateVersion7();
        var changedAt = At(7, 41);
        var moved = View(
            journeyId,
            [
                Stop(1, Alpha, PlatformTwo, departure: At(8, 0), expectedDeparture: At(8, 2), platformChangedAt: changedAt),
                Stop(2, Gamma, arrival: At(8, 20)),
            ]);

        var oldPlatform = Feed([moved], PlatformOne);

        Assert.Empty(oldPlatform.Departures);

        var notice = Assert.Single(oldPlatform.MovedNotices);
        Assert.Equal(journeyId, notice.JourneyId);
        Assert.Equal(Gamma, notice.Destination.Id);
        Assert.Equal(At(8, 0), notice.ScheduledDeparture);
        Assert.Equal("2", notice.NewPlatform.Number);
        Assert.Equal("The 08:00 service to Gamma has moved to platform 2.", notice.Message);

        // It is a departure of the platform it was assigned to, flagged with the time it changed.
        var newPlatform = Feed([moved], PlatformTwo);

        var departure = Assert.Single(newPlatform.Departures);
        Assert.Equal(changedAt, departure.Origin.PlatformChangedAt);
        Assert.Empty(newPlatform.MovedNotices);
    }

    [Fact]
    public void The_notice_ends_once_the_scheduled_departure_has_passed()
    {
        var moved = View(
            Guid.CreateVersion7(),
            [
                Stop(1, Alpha, PlatformTwo, departure: At(8, 0), expectedDeparture: At(8, 20), platformChangedAt: At(7, 41)),
                Stop(2, Gamma, arrival: At(8, 40)),
            ]);

        // A minute past the scheduled departure: the old platform stops announcing the train, even
        // though the delayed train is still listed on its new platform.
        var oldPlatform = Feed([moved], PlatformOne, now: At(8, 1));
        var stillListed = Feed([moved], PlatformOne, now: At(7, 59));

        Assert.Empty(oldPlatform.MovedNotices);
        Assert.Single(stillListed.MovedNotices);
    }

    [Fact]
    public void A_train_on_its_timetabled_platform_has_no_notice()
    {
        var journey = View(
            Guid.CreateVersion7(),
            [Stop(1, Alpha, PlatformOne, departure: At(8, 0), expectedDeparture: At(8, 0)), Stop(2, Gamma, arrival: At(8, 20))]);

        var feed = Feed([journey], PlatformOne);

        Assert.Single(feed.Departures);
        Assert.Empty(feed.MovedNotices);
    }

    /// <summary>
    /// The feed of one platform at the fixture's first station, read at the fixture's own clock
    /// unless a test names another instant.
    /// </summary>
    private static PublicPlatformFeed Feed(
        IReadOnlyList<PublicJourneyView> journeys,
        Guid platformId,
        int count = PublicPlatformSelection.DefaultCount,
        DateTimeOffset? now = null) =>
        PublicPlatformSelection.Feed(
            journeys,
            Alpha,
            platformId,
            count,
            now ?? At(7, 50),
            Lookup(),
            TimeZoneInfo.Utc);

    private static PublicJourneyView View(Guid journeyId, IReadOnlyList<LiveStopView> stops) =>
        PublicJourneyViewBuilder.Build(
            PublicJourneyViewFixture.Journey(stops: stops, journeyId: journeyId),
            Lookup());

    /// <summary>
    /// The fixture's catalogue with the timetable's own stop at the first station on platform 1, so
    /// the selection can tell the platform a journey was assigned before its change.
    /// </summary>
    private static NetworkLookup Lookup() =>
        PublicJourneyViewFixture.Lookup() with
        {
            Services = new Dictionary<Guid, ServiceDto>
            {
                [PublicJourneyViewFixture.ServiceId] = new(
                    PublicJourneyViewFixture.ServiceId,
                    PublicJourneyViewFixture.ServiceName,
                    PublicJourneyViewFixture.LineId,
                    ServiceDirection.Outbound,
                    PublicJourneyViewFixture.GuardStaffId,
                    [
                        new ServiceStopDto(1, Alpha, IsCalling: true, ScheduledArrival: null, ScheduledDeparture: new TimeOnly(8, 0), PlatformId: PlatformOne),
                        new ServiceStopDto(2, Beta, IsCalling: false, ScheduledArrival: null, ScheduledDeparture: null, PlatformId: null),
                        new ServiceStopDto(3, Gamma, IsCalling: true, new TimeOnly(8, 20), ScheduledDeparture: null, PlatformId: null),
                    ]),
            },
        };

    private static LiveStopView Stop(
        int sequence,
        Guid stationId,
        Guid? platformId = null,
        DateTimeOffset? departure = null,
        DateTimeOffset? expectedDeparture = null,
        DateTimeOffset? arrival = null,
        DateTimeOffset? platformChangedAt = null) =>
        new(
            sequence,
            stationId,
            IsCalling: true,
            ScheduledArrival: arrival,
            departure,
            ExpectedArrival: null,
            expectedDeparture,
            ActualDeparture: null,
            platformId,
            platformChangedAt);

    private static DateTimeOffset At(int hour, int minute) => PublicJourneyViewFixture.At(hour, minute);
}
