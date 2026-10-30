using SmartRail.Distribution.Application.Public;
using SmartRail.LiveOperations.Contracts;
using SmartRail.RailNetwork.Contracts;

namespace SmartRail.Distribution.UnitTests.Public;

/// <summary>
/// What the public view shows of one journey (passenger-departures spec, journey detail and platform
/// changes): calling and non-calling stops, the current and a changed platform with its provenance,
/// a cancellation, a termination and a correction, and unknown identifiers that must not fail the
/// view.
/// </summary>
public sealed class PublicJourneyViewBuilderTests
{
    [Fact]
    public void A_non_calling_stop_is_marked_and_carries_no_times_and_no_platform()
    {
        var view = PublicJourneyViewBuilder.Build(PublicJourneyViewFixture.Journey(), PublicJourneyViewFixture.Lookup());

        var skipped = Assert.Single(view.Stops, stop => !stop.IsCalling);

        Assert.Equal(2, skipped.Sequence);
        Assert.Empty(
            new[] { skipped.ScheduledArrival, skipped.ScheduledDeparture, skipped.ExpectedArrival, skipped.ExpectedDeparture, skipped.ActualDeparture }
                .OfType<DateTimeOffset>());
        Assert.Null(skipped.Platform);
        Assert.Null(skipped.PlatformChangedAt);
        Assert.Null(skipped.Provenance);

        // The station is still named, so a passenger sees which station the train passes.
        Assert.Equal(new PublicStationRef(PublicJourneyViewFixture.Beta, "Beta"), skipped.Station);
    }

    [Fact]
    public void A_non_calling_stop_drops_times_and_platform_even_when_the_live_state_holds_them()
    {
        var stops = PublicJourneyViewFixture.Stops()
            .Select(stop => stop.Sequence == 2
                ? stop with
                {
                    ScheduledArrival = PublicJourneyViewFixture.At(8, 10),
                    ExpectedDeparture = PublicJourneyViewFixture.At(8, 12),
                    PlatformId = PublicJourneyViewFixture.AlphaPlatformOne,
                    PlatformChangedAt = PublicJourneyViewFixture.At(7, 45),
                }
                : stop)
            .ToArray();

        var view = PublicJourneyViewBuilder.Build(
            PublicJourneyViewFixture.Journey(stops: stops),
            PublicJourneyViewFixture.Lookup());

        var skipped = view.Stops[1];
        Assert.False(skipped.IsCalling);
        Assert.Null(skipped.ScheduledArrival);
        Assert.Null(skipped.ExpectedDeparture);
        Assert.Null(skipped.Platform);
        Assert.Null(skipped.PlatformChangedAt);
        Assert.Null(skipped.Provenance);
    }

    [Fact]
    public void The_current_platform_is_shown_without_a_change_time_or_a_label()
    {
        var view = PublicJourneyViewBuilder.Build(PublicJourneyViewFixture.Journey(), PublicJourneyViewFixture.Lookup());

        var first = view.Stops[0];
        Assert.Equal(new PublicPlatformRef(PublicJourneyViewFixture.AlphaPlatformOne, "1"), first.Platform);
        Assert.Null(first.PlatformChangedAt);
        Assert.Null(first.Provenance);
        Assert.Null(view.Provenance);
        Assert.Null(view.CorrectedAt);
    }

    [Fact]
    public void A_changed_platform_carries_the_time_it_became_known_and_the_label()
    {
        var changedAt = PublicJourneyViewFixture.At(7, 41);

        var view = PublicJourneyViewBuilder.Build(
            PublicJourneyViewFixture.Journey(stops: PublicJourneyViewFixture.Stops(
                firstPlatformChangedAt: changedAt,
                firstPlatformId: PublicJourneyViewFixture.AlphaPlatformTwo)),
            PublicJourneyViewFixture.Lookup());

        var first = view.Stops[0];
        Assert.Equal(new PublicPlatformRef(PublicJourneyViewFixture.AlphaPlatformTwo, "2"), first.Platform);
        Assert.Equal(changedAt, first.PlatformChangedAt);
        Assert.Equal(PublicProvenance.AutomaticallyPublished, first.Provenance);

        // The journey as a whole carries the label while that change is in force.
        Assert.Equal(PublicProvenance.AutomaticallyPublished, view.Provenance);
    }

    [Fact]
    public void A_cancelled_journey_carries_the_label_and_no_expected_times()
    {
        var view = PublicJourneyViewBuilder.Build(
            PublicJourneyViewFixture.Journey(
                status: JourneyLifecycleStatus.Cancelled,
                stops: PublicJourneyViewFixture.Stops().Select(stop => stop with
                {
                    ExpectedArrival = null,
                    ExpectedDeparture = null,
                }).ToArray()),
            PublicJourneyViewFixture.Lookup());

        Assert.Equal(JourneyLifecycleStatus.Cancelled, view.Status);
        Assert.Equal(PublicProvenance.AutomaticallyPublished, view.Provenance);
        Assert.All(view.Stops.Where(stop => stop.IsCalling), stop =>
        {
            Assert.Null(stop.ExpectedArrival);
            Assert.Null(stop.ExpectedDeparture);
            Assert.NotNull(stop.ScheduledDeparture ?? stop.ScheduledArrival);
        });
    }

    [Fact]
    public void A_terminated_journey_keeps_scheduled_times_after_its_last_served_stop()
    {
        var stops = new[]
        {
            new LiveStopView(1, PublicJourneyViewFixture.Alpha, IsCalling: true, ScheduledArrival: null,
                ScheduledDeparture: PublicJourneyViewFixture.At(8, 0), ExpectedArrival: null,
                ExpectedDeparture: PublicJourneyViewFixture.At(8, 5), ActualDeparture: PublicJourneyViewFixture.At(8, 5),
                PlatformId: PublicJourneyViewFixture.AlphaPlatformOne, PlatformChangedAt: null),
            new LiveStopView(2, PublicJourneyViewFixture.Beta, IsCalling: true, ScheduledArrival: null,
                ScheduledDeparture: PublicJourneyViewFixture.At(8, 20), ExpectedArrival: null,
                ExpectedDeparture: null, ActualDeparture: null, PlatformId: null, PlatformChangedAt: null),
            new LiveStopView(3, PublicJourneyViewFixture.Gamma, IsCalling: true, ScheduledArrival: PublicJourneyViewFixture.At(8, 40),
                ScheduledDeparture: null, ExpectedArrival: null, ExpectedDeparture: null, ActualDeparture: null,
                PlatformId: null, PlatformChangedAt: null),
        };

        var view = PublicJourneyViewBuilder.Build(
            PublicJourneyViewFixture.Journey(status: JourneyLifecycleStatus.Terminated, stops: stops),
            PublicJourneyViewFixture.Lookup());

        Assert.Equal(PublicProvenance.AutomaticallyPublished, view.Provenance);
        Assert.Equal(PublicJourneyViewFixture.At(8, 5), view.Stops[0].ActualDeparture);
        Assert.Equal(PublicJourneyViewFixture.At(8, 20), view.Stops[1].ScheduledDeparture);
        Assert.Null(view.Stops[1].ExpectedDeparture);
        Assert.Equal(PublicJourneyViewFixture.At(8, 40), view.Stops[2].ScheduledArrival);
        Assert.Null(view.Stops[2].ExpectedArrival);
    }

    [Fact]
    public void A_corrected_journey_carries_the_correction_time_and_no_label()
    {
        var correctedAt = PublicJourneyViewFixture.At(8, 10);

        var view = PublicJourneyViewBuilder.Build(
            PublicJourneyViewFixture.Journey(
                status: JourneyLifecycleStatus.Running,
                correctedAt: correctedAt),
            PublicJourneyViewFixture.Lookup());

        Assert.Equal(correctedAt, view.CorrectedAt);
        Assert.Null(view.Provenance);
        Assert.Null(view.Guidance);
    }

    [Fact]
    public void The_journey_names_come_from_the_catalogue_and_service_names_from_the_timetable()
    {
        var view = PublicJourneyViewBuilder.Build(PublicJourneyViewFixture.Journey(), PublicJourneyViewFixture.Lookup());

        Assert.Equal(PublicJourneyViewFixture.ServiceName, view.ServiceName);
        Assert.Equal(ServiceDirection.Outbound, view.Direction);
        Assert.Equal(
            new PublicLineRef(PublicJourneyViewFixture.LineId, "L1", "#1d4ed8", "Main Line"),
            view.Line);
        Assert.Equal(new PublicStationRef(PublicJourneyViewFixture.Gamma, "Gamma"), view.NextStation);
        Assert.Equal(PublicJourneyViewFixture.ServiceDate, view.ServiceDate);
        Assert.Equal(PublicJourneyViewFixture.At(7, 30), view.LastUpdatedAt);
    }

    [Fact]
    public void Unknown_identifiers_leave_null_names_and_never_throw()
    {
        var unknownStation = Guid.CreateVersion7();
        var unknownPlatform = Guid.CreateVersion7();
        var stops = new[]
        {
            new LiveStopView(1, unknownStation, IsCalling: true, ScheduledArrival: null,
                ScheduledDeparture: PublicJourneyViewFixture.At(8, 0), ExpectedArrival: null,
                ExpectedDeparture: PublicJourneyViewFixture.At(8, 0), ActualDeparture: null,
                PlatformId: unknownPlatform, PlatformChangedAt: null),
        };

        var view = PublicJourneyViewBuilder.Build(
            PublicJourneyViewFixture.Journey(
                stops: stops,
                serviceId: Guid.CreateVersion7(),
                lineId: Guid.CreateVersion7()),
            PublicJourneyViewFixture.Lookup());

        Assert.Null(view.ServiceName);
        Assert.Null(view.Direction);
        Assert.Null(view.Line.Code);
        Assert.Null(view.Line.Color);
        Assert.Null(view.Line.Name);
        Assert.Null(view.Stops[0].Station.Name);
        Assert.Equal(unknownStation, view.Stops[0].Station.Id);
        Assert.Null(view.Stops[0].Platform?.Number);
        Assert.Equal(unknownPlatform, view.Stops[0].Platform?.Id);
    }
}
