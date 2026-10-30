using SmartRail.Distribution.Application.Public;
using SmartRail.Distribution.Application.Public.Departures;
using SmartRail.LiveOperations.Contracts;

namespace SmartRail.Distribution.UnitTests.Public;

/// <summary>
/// The rules of the passenger departure lists (passenger-departures spec): the direct journey, the
/// station passed without calling, the wrong direction, what still counts as upcoming for a journey
/// that just left, a cancelled one and one ended early, the window bounds, the order, and the
/// destination and minutes late each item carries.
/// </summary>
public sealed class PublicDepartureSelectionTests
{
    private static readonly Guid Alpha = PublicJourneyViewFixture.Alpha;
    private static readonly Guid Beta = PublicJourneyViewFixture.Beta;
    private static readonly Guid Gamma = PublicJourneyViewFixture.Gamma;

    [Fact]
    public void A_journey_that_calls_at_both_stations_is_listed_with_its_origin_times_and_platform()
    {
        var journeyId = Guid.CreateVersion7();
        var journey = PublicJourneyViewFixture.View(
            stops: DefaultStops(),
            journeyId: journeyId);

        var departures = PublicDepartureSelection.Between(
            [journey],
            Alpha,
            Gamma,
            PublicJourneyViewFixture.At(7, 50));

        var departure = Assert.Single(departures);
        Assert.Equal(journeyId, departure.Journey.JourneyId);
        Assert.Equal(1, departure.Origin.Sequence);
        Assert.Equal(Alpha, departure.Origin.Station.Id);
        Assert.Equal(PublicJourneyViewFixture.At(8, 0), departure.Origin.ScheduledDeparture);
        Assert.Equal(PublicJourneyViewFixture.At(8, 2), departure.Origin.ExpectedDeparture);
        Assert.Equal("1", departure.Origin.Platform?.Number);
        Assert.Equal(Gamma, departure.Destination.Id);
        Assert.Equal("Gamma", departure.Destination.Name);
    }

    [Fact]
    public void A_journey_that_passes_the_destination_without_calling_is_not_listed()
    {
        // Beta is passed without calling: it is a station on the line, but not a stop of the journey.
        var journey = PublicJourneyViewFixture.View(stops: DefaultStops());

        var departures = PublicDepartureSelection.Between(
            [journey],
            Alpha,
            Beta,
            PublicJourneyViewFixture.At(7, 50));

        Assert.Empty(departures);
    }

    [Fact]
    public void A_journey_that_reaches_the_destination_first_is_not_listed()
    {
        var journey = PublicJourneyViewFixture.View(stops: ReverseStops());

        var departures = PublicDepartureSelection.Between(
            [journey],
            Alpha,
            Gamma,
            PublicJourneyViewFixture.At(7, 50));

        Assert.Empty(departures);
    }

    [Fact]
    public void A_journey_that_departed_within_two_minutes_is_still_listed_with_its_actual_departure()
    {
        var journey = PublicJourneyViewFixture.View(
            status: JourneyLifecycleStatus.Running,
            stops: DepartedStops());

        var stillListed = PublicDepartureSelection.Between(
            [journey],
            Alpha,
            Gamma,
            PublicJourneyViewFixture.At(8, 1));

        var departure = Assert.Single(stillListed);
        Assert.Equal(PublicJourneyViewFixture.At(8, 0), departure.Origin.ActualDeparture);

        // Three minutes after the departure the train is no longer upcoming at Alpha.
        var noLongerListed = PublicDepartureSelection.Between(
            [journey],
            Alpha,
            Gamma,
            PublicJourneyViewFixture.At(8, 3));

        Assert.Empty(noLongerListed);
    }

    [Fact]
    public void A_cancelled_journey_stays_listed_until_ten_minutes_after_its_scheduled_departure()
    {
        var journey = PublicJourneyViewFixture.View(
            status: JourneyLifecycleStatus.Cancelled,
            stops: CancelledStops());

        var stillListed = PublicDepartureSelection.FromStation(
            [journey],
            Alpha,
            PublicDepartureSelection.DefaultWindowMinutes,
            PublicJourneyViewFixture.At(8, 5));

        var departure = Assert.Single(stillListed);
        Assert.Equal(JourneyLifecycleStatus.Cancelled, departure.Journey.Status);
        Assert.Null(departure.Origin.ExpectedDeparture);
        Assert.Null(departure.MinutesLate);

        // Eleven minutes past the scheduled departure the cancelled journey is no longer shown.
        var noLongerListed = PublicDepartureSelection.FromStation(
            [journey],
            Alpha,
            PublicDepartureSelection.DefaultWindowMinutes,
            PublicJourneyViewFixture.At(8, 11));

        Assert.Empty(noLongerListed);
    }

    [Fact]
    public void An_early_terminated_journey_is_listed_after_its_last_served_stop_with_no_expected_departure()
    {
        var journey = PublicJourneyViewFixture.View(
            status: JourneyLifecycleStatus.Terminated,
            stops: TerminatedStops());

        var stillListed = PublicDepartureSelection.FromStation(
            [journey],
            Gamma,
            PublicDepartureSelection.DefaultWindowMinutes,
            PublicJourneyViewFixture.At(8, 5));

        var departure = Assert.Single(stillListed);
        Assert.Equal(JourneyLifecycleStatus.Terminated, departure.Journey.Status);
        Assert.Equal(PublicJourneyViewFixture.At(8, 20), departure.Origin.ScheduledDeparture);
        Assert.Null(departure.Origin.ExpectedDeparture);
        Assert.Null(departure.MinutesLate);

        // The terminated journey is listed at a station after the last served one until its
        // scheduled departure has passed by ten minutes.
        var noLongerListed = PublicDepartureSelection.FromStation(
            [journey],
            Gamma,
            PublicDepartureSelection.DefaultWindowMinutes,
            PublicJourneyViewFixture.At(8, 31));

        Assert.Empty(noLongerListed);
    }

    [Fact]
    public void A_final_calling_stop_is_not_a_departure_for_its_station()
    {
        // Gamma is the terminus: it has an arrival and no departure, so it has no departure to list.
        var journey = PublicJourneyViewFixture.View(stops: DefaultStops());

        var departures = PublicDepartureSelection.FromStation(
            [journey],
            Gamma,
            PublicDepartureSelection.DefaultWindowMinutes,
            PublicJourneyViewFixture.At(7, 50));

        Assert.Empty(departures);
    }

    [Fact]
    public void The_window_bounds_which_departures_are_returned()
    {
        Assert.Equal(120, PublicDepartureSelection.DefaultWindowMinutes);
        Assert.Equal(360, PublicDepartureSelection.MaximumWindowMinutes);

        var inside = PublicJourneyViewFixture.View(
            stops: [Stop(1, Alpha, departure: At(10, 0), expectedDeparture: At(10, 0)), Stop(2, Gamma, arrival: At(10, 20))],
            journeyId: Guid.CreateVersion7());
        var beyond = PublicJourneyViewFixture.View(
            stops: [Stop(1, Alpha, departure: At(10, 1), expectedDeparture: At(10, 1)), Stop(2, Gamma, arrival: At(10, 20))],
            journeyId: Guid.CreateVersion7());

        var departures = PublicDepartureSelection.FromStation(
            [inside, beyond],
            Alpha,
            PublicDepartureSelection.DefaultWindowMinutes,
            PublicJourneyViewFixture.At(8, 0));

        var departure = Assert.Single(departures);
        Assert.Equal(inside.JourneyId, departure.Journey.JourneyId);
    }

    [Fact]
    public void Departures_are_ordered_by_their_expected_departure()
    {
        var delayed = PublicJourneyViewFixture.View(
            stops: [Stop(1, Alpha, departure: At(8, 0), expectedDeparture: At(8, 30)), Stop(2, Gamma, arrival: At(8, 40))],
            journeyId: Guid.CreateVersion7());
        var onTime = PublicJourneyViewFixture.View(
            stops: [Stop(1, Alpha, departure: At(8, 10), expectedDeparture: At(8, 10)), Stop(2, Gamma, arrival: At(8, 20))],
            journeyId: Guid.CreateVersion7());

        var departures = PublicDepartureSelection.FromStation(
            [delayed, onTime],
            Alpha,
            PublicDepartureSelection.DefaultWindowMinutes,
            PublicJourneyViewFixture.At(7, 50));

        Assert.Equal(
            [onTime.JourneyId, delayed.JourneyId],
            departures.Select(departure => departure.Journey.JourneyId).ToArray());
    }

    [Fact]
    public void The_destination_is_the_final_calling_stop_not_the_station_asked_about()
    {
        var journey = PublicJourneyViewFixture.View(
            stops:
            [
                Stop(1, Alpha, departure: At(8, 0), expectedDeparture: At(8, 0)),
                Stop(2, Beta, departure: At(8, 10), expectedDeparture: At(8, 10)),
                Stop(3, Gamma, arrival: At(8, 20)),
            ],
            journeyId: Guid.CreateVersion7());

        var departure = Assert.Single(PublicDepartureSelection.Between(
            [journey],
            Alpha,
            Beta,
            PublicJourneyViewFixture.At(7, 50)));

        Assert.Equal(Alpha, departure.Origin.Station.Id);

        // The journey called at Beta on the way, but the departure goes to its final calling stop.
        Assert.Contains(departure.Journey.Stops, stop => stop.IsCalling && stop.Station.Id == Beta);
        Assert.Equal(Gamma, departure.Destination.Id);    }

    [Fact]
    public void Minutes_late_comes_from_the_live_departure_at_that_stop()
    {
        var late = PublicJourneyViewFixture.View(
            stops: [Stop(1, Alpha, departure: At(8, 0), expectedDeparture: At(8, 7)), Stop(2, Gamma, arrival: At(8, 20))],
            journeyId: Guid.CreateVersion7());
        var early = PublicJourneyViewFixture.View(
            stops: [Stop(1, Alpha, departure: At(8, 0), expectedDeparture: At(7, 58)), Stop(2, Gamma, arrival: At(8, 20))],
            journeyId: Guid.CreateVersion7());

        var departures = PublicDepartureSelection.FromStation(
            [late, early],
            Alpha,
            PublicDepartureSelection.DefaultWindowMinutes,
            PublicJourneyViewFixture.At(7, 50));

        // Ordered by expected departure: the early train first.
        Assert.Equal(early.JourneyId, departures[0].Journey.JourneyId);
        Assert.Equal(-2, departures[0].MinutesLate);
        Assert.Equal(7, departures[1].MinutesLate);
    }

    /// <summary>The fixture's default stops: Alpha calls, Beta is passed without calling, Gamma is the terminus.</summary>
    private static IReadOnlyList<LiveStopView> DefaultStops() => PublicJourneyViewFixture.Stops();

    /// <summary>The same journey in the opposite direction: Gamma first and Alpha last.</summary>
    private static IReadOnlyList<LiveStopView> ReverseStops() =>
    [
        Stop(1, Gamma, departure: At(8, 0), expectedDeparture: At(8, 0)),
        Stop(2, Beta, isCalling: false),
        Stop(3, Alpha, arrival: At(8, 20)),
    ];

    /// <summary>Alpha has departed with the recorded actual departure; Gamma is the terminus.</summary>
    private static IReadOnlyList<LiveStopView> DepartedStops() =>
    [
        Stop(1, Alpha, departure: At(8, 0), expectedDeparture: At(8, 0), actualDeparture: At(8, 0)),
        Stop(2, Gamma, arrival: At(8, 20)),
    ];

    /// <summary>A cancelled journey reports no expected times at all.</summary>
    private static IReadOnlyList<LiveStopView> CancelledStops() =>
    [
        Stop(1, Alpha, departure: At(8, 0)),
        Stop(2, Gamma, arrival: At(8, 20)),
    ];

    /// <summary>
    /// A journey ended early at Beta: Alpha and Beta have expected times, Gamma after the last
    /// served stop has none.
    /// </summary>
    private static IReadOnlyList<LiveStopView> TerminatedStops() =>
    [
        Stop(1, Alpha, departure: At(8, 0), expectedDeparture: At(8, 0), actualDeparture: At(8, 0)),
        Stop(2, Beta, departure: At(8, 10), expectedDeparture: At(8, 10), actualDeparture: At(8, 10)),
        Stop(3, Gamma, departure: At(8, 20)),
    ];

    private static LiveStopView Stop(
        int sequence,
        Guid stationId,
        DateTimeOffset? departure = null,
        DateTimeOffset? expectedDeparture = null,
        DateTimeOffset? actualDeparture = null,
        DateTimeOffset? arrival = null,
        bool isCalling = true) =>
        new(
            sequence,
            stationId,
            isCalling,
            arrival,
            departure,
            ExpectedArrival: null,
            expectedDeparture,
            actualDeparture,
            PlatformId: null,
            PlatformChangedAt: null);

    private static DateTimeOffset At(int hour, int minute) => PublicJourneyViewFixture.At(hour, minute);
}
