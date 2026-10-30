using SmartRail.Distribution.Application.Public;
using SmartRail.Distribution.Application.Public.Guidance;
using SmartRail.LiveOperations.Contracts;

namespace SmartRail.Distribution.UnitTests.Public;

/// <summary>
/// What guidance a disrupted journey carries (disruption-guidance spec): the fixed template for each
/// disruption, the five-minute delay threshold, the next service to the same destination or the
/// sentence that none runs today, the priority when a journey carries several disruptions, and the
/// railway's clock in every time it writes.
/// </summary>
public sealed class GuidanceBuilderTests
{
    [Fact]
    public void A_cancellation_names_the_departure_and_the_next_service_with_its_expected_time_and_platform()
    {
        var cancelled = GuidanceFixture.Journey(
            status: JourneyLifecycleStatus.Cancelled,
            stops: GuidanceFixture.CancelledStops());
        var next = GuidanceFixture.Candidate(
            GuidanceFixture.At(8, 20),
            GuidanceFixture.At(8, 25),
            originPlatform: GuidanceFixture.AlphaPlatformTwo);

        var guidance = Build(cancelled, cancelled, next);

        Assert.NotNull(guidance);
        Assert.Equal(PublicGuidanceType.Cancellation, guidance.Type);
        Assert.Equal(
            "The 08:00 Main Line service to Gamma is cancelled. Please take the next service. "
                + "The next service is the 08:20 to Gamma, expected at 08:25 from platform 2.",
            guidance.Message);
        Assert.False(guidance.NoLaterService);

        var nextService = Assert.IsType<PublicNextService>(guidance.NextService);
        Assert.Equal(next.JourneyId, nextService.JourneyId);
        Assert.Equal(GuidanceFixture.OtherServiceName, nextService.ServiceName);
        Assert.Equal(GuidanceFixture.At(8, 20), nextService.ScheduledDeparture);
        Assert.Equal(GuidanceFixture.At(8, 25), nextService.ExpectedDeparture);
        Assert.Equal(new PublicPlatformRef(GuidanceFixture.AlphaPlatformTwo, "2"), nextService.Platform);
        Assert.Equal(new PublicStationRef(GuidanceFixture.Alpha, "Alpha"), nextService.Station);
    }

    [Fact]
    public void An_early_termination_names_the_last_station_served_and_the_next_service()
    {
        var terminated = GuidanceFixture.Journey(
            status: JourneyLifecycleStatus.Terminated,
            delayMinutes: null,
            stops: GuidanceFixture.TerminatedStops());
        var next = GuidanceFixture.Candidate(GuidanceFixture.At(8, 20), GuidanceFixture.At(8, 25));

        var guidance = Build(terminated, terminated, next);

        Assert.NotNull(guidance);
        Assert.Equal(PublicGuidanceType.Termination, guidance.Type);
        Assert.Equal(
            "The 08:00 Main Line service to Gamma will not continue beyond Beta. "
                + "It will not call at the stations after Beta. Please take the next service. "
                + "The next service is the 08:20 to Gamma, expected at 08:25 from platform 1.",
            guidance.Message);
        Assert.Equal(next.JourneyId, guidance.NextService?.JourneyId);
    }

    [Fact]
    public void A_platform_change_names_the_new_platform_and_its_station()
    {
        var changed = GuidanceFixture.Journey(stops: ChangedPlatforms(
            (Sequence: 1, Platform: GuidanceFixture.AlphaPlatformTwo)));

        var guidance = Build(changed, changed);

        Assert.NotNull(guidance);
        Assert.Equal(PublicGuidanceType.PlatformChange, guidance.Type);
        Assert.Equal(
            "The 08:00 Main Line service to Gamma now departs from platform 2 at Alpha.",
            guidance.Message);
        Assert.Null(guidance.NextService);
        Assert.False(guidance.NoLaterService);
    }

    [Fact]
    public void The_earliest_stop_whose_platform_changed_and_that_has_not_departed_is_named()
    {
        var changed = GuidanceFixture.Journey(stops: ChangedPlatforms(
            (Sequence: 1, Platform: GuidanceFixture.AlphaPlatformTwo),
            (Sequence: 3, Platform: GuidanceFixture.GammaPlatformOne)));

        var guidance = Build(changed, changed);

        var message = Assert.IsType<PublicGuidance>(guidance).Message;
        Assert.Contains("from platform 2 at Alpha", message, StringComparison.Ordinal);
        Assert.DoesNotContain("at Gamma", message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_platform_change_at_a_stop_that_already_departed_is_not_reported()
    {
        var departed = GuidanceFixture.Route(GuidanceFixture.At(8, 0), GuidanceFixture.At(8, 0))
            .Select(stop => stop.Sequence == 1
                ? GuidanceFixture.WithPlatformChange(
                    stop with { ActualDeparture = GuidanceFixture.At(8, 0) },
                    GuidanceFixture.AlphaPlatformTwo,
                    GuidanceFixture.At(7, 41))
                : stop)
            .ToArray();

        var journey = GuidanceFixture.Journey(stops: departed);

        Assert.Null(Build(journey, journey));
    }

    [Fact]
    public void A_delay_of_four_minutes_carries_no_guidance()
    {
        var journey = GuidanceFixture.Journey(delayMinutes: 4);

        Assert.Null(Build(journey, journey));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(14)]
    public void A_delay_from_five_to_fourteen_minutes_carries_the_sentence_without_a_next_service(int minutes)
    {
        var journey = GuidanceFixture.Journey(delayMinutes: minutes);
        var next = GuidanceFixture.Candidate(GuidanceFixture.At(8, 20), GuidanceFixture.At(8, 25));

        var guidance = Build(journey, journey, next);

        Assert.NotNull(guidance);
        Assert.Equal(PublicGuidanceType.Delay, guidance.Type);
        Assert.Equal(
            $"The 08:00 Main Line service to Gamma is running {minutes} minutes late.",
            guidance.Message);
        Assert.Null(guidance.NextService);
        Assert.False(guidance.NoLaterService);
    }

    [Fact]
    public void A_delay_of_fifteen_minutes_also_names_the_next_service()
    {
        var journey = GuidanceFixture.Journey(delayMinutes: 15);
        var next = GuidanceFixture.Candidate(
            GuidanceFixture.At(8, 20),
            GuidanceFixture.At(8, 25),
            originPlatform: GuidanceFixture.AlphaPlatformTwo);

        var guidance = Build(journey, journey, next);

        Assert.NotNull(guidance);
        Assert.Equal(PublicGuidanceType.Delay, guidance.Type);
        Assert.Equal(
            "The 08:00 Main Line service to Gamma is running 15 minutes late. "
                + "The next service is the 08:20 to Gamma, expected at 08:25 from platform 2.",
            guidance.Message);
        Assert.Equal(next.JourneyId, guidance.NextService?.JourneyId);
    }

    [Fact]
    public void An_early_journey_carries_no_guidance()
    {
        var journey = GuidanceFixture.Journey(delayMinutes: -3);

        Assert.Null(Build(journey, journey));
    }

    [Fact]
    public void An_undisrupted_journey_carries_no_guidance()
    {
        var journey = GuidanceFixture.Journey(delayMinutes: 0);

        Assert.Null(Build(journey, journey));
    }

    [Fact]
    public void A_corrected_cancellation_carries_no_guidance()
    {
        var corrected = GuidanceFixture.Journey(
            status: JourneyLifecycleStatus.Scheduled,
            delayMinutes: 0,
            correctedAt: GuidanceFixture.At(8, 10));

        Assert.Null(Build(corrected, corrected));
    }

    [Fact]
    public void The_last_train_of_the_day_says_that_no_later_service_runs_today()
    {
        var cancelled = GuidanceFixture.Journey(
            status: JourneyLifecycleStatus.Cancelled,
            stops: GuidanceFixture.CancelledStops());

        var guidance = Build(cancelled);

        Assert.NotNull(guidance);
        Assert.Equal(
            "The 08:00 Main Line service to Gamma is cancelled. Please take the next service. "
                + "No later service is scheduled today.",
            guidance.Message);
        Assert.True(guidance.NoLaterService);
        Assert.Null(guidance.NextService);
    }

    [Fact]
    public void The_next_service_skips_a_cancelled_a_terminated_an_already_gone_and_a_different_destination_journey()
    {
        var cancelled = GuidanceFixture.Journey(
            status: JourneyLifecycleStatus.Cancelled,
            stops: GuidanceFixture.CancelledStops());

        var cancelledCandidate = GuidanceFixture.Candidate(
            GuidanceFixture.At(8, 5),
            GuidanceFixture.At(8, 5),
            status: JourneyLifecycleStatus.Cancelled);
        var terminatedCandidate = GuidanceFixture.Candidate(
            GuidanceFixture.At(8, 8),
            GuidanceFixture.At(8, 8),
            status: JourneyLifecycleStatus.Terminated);
        var completedCandidate = GuidanceFixture.Candidate(
            GuidanceFixture.At(8, 12),
            GuidanceFixture.At(8, 12),
            status: JourneyLifecycleStatus.Completed);
        var elsewhere = GuidanceFixture.Candidate(
            GuidanceFixture.At(8, 15),
            GuidanceFixture.At(8, 15),
            destination: GuidanceFixture.Delta);
        var alreadyDeparted = GuidanceFixture.Candidate(GuidanceFixture.At(7, 50), GuidanceFixture.At(7, 55));
        var following = GuidanceFixture.Candidate(GuidanceFixture.At(8, 20), GuidanceFixture.At(8, 25));

        var guidance = Build(
            cancelled,
            cancelled,
            cancelledCandidate,
            terminatedCandidate,
            completedCandidate,
            elsewhere,
            alreadyDeparted,
            following);

        Assert.NotNull(guidance);
        Assert.Equal(following.JourneyId, guidance.NextService?.JourneyId);
        Assert.Contains("08:20", guidance.Message, StringComparison.Ordinal);
        Assert.False(guidance.NoLaterService);
    }

    [Fact]
    public void A_cancellation_is_reported_before_an_early_termination_a_platform_change_and_a_delay()
    {
        var everything = GuidanceFixture.Journey(
            status: JourneyLifecycleStatus.Cancelled,
            delayMinutes: 20,
            stops: ChangedPlatforms((Sequence: 1, Platform: GuidanceFixture.AlphaPlatformTwo)));

        var guidance = Build(everything, everything, Next());

        Assert.NotNull(guidance);
        Assert.Equal(PublicGuidanceType.Cancellation, guidance.Type);
    }

    [Fact]
    public void An_early_termination_is_reported_before_a_platform_change_and_a_delay()
    {
        var everything = GuidanceFixture.Journey(
            status: JourneyLifecycleStatus.Terminated,
            delayMinutes: 20,
            stops: GuidanceFixture.TerminatedStops()
                .Select(stop => stop.Sequence == 1
                    ? GuidanceFixture.WithPlatformChange(
                        stop,
                        GuidanceFixture.AlphaPlatformTwo,
                        GuidanceFixture.At(7, 41))
                    : stop)
                .ToArray());

        var guidance = Build(everything, everything, Next());

        Assert.NotNull(guidance);
        Assert.Equal(PublicGuidanceType.Termination, guidance.Type);
    }

    [Fact]
    public void A_platform_change_is_reported_before_a_delay()
    {
        var everything = GuidanceFixture.Journey(
            delayMinutes: 20,
            stops: ChangedPlatforms((Sequence: 1, Platform: GuidanceFixture.AlphaPlatformTwo)));

        var guidance = Build(everything, everything, Next());

        Assert.NotNull(guidance);
        Assert.Equal(PublicGuidanceType.PlatformChange, guidance.Type);
    }

    [Fact]
    public void Every_message_is_a_complete_sentence_ending_with_a_full_stop()
    {
        var cancelled = GuidanceFixture.Journey(
            status: JourneyLifecycleStatus.Cancelled,
            stops: GuidanceFixture.CancelledStops());
        var terminated = GuidanceFixture.Journey(
            status: JourneyLifecycleStatus.Terminated,
            delayMinutes: null,
            stops: GuidanceFixture.TerminatedStops());
        var changedPlatform = GuidanceFixture.Journey(
            stops: ChangedPlatforms((Sequence: 1, Platform: GuidanceFixture.AlphaPlatformTwo)));
        var delayed = GuidanceFixture.Journey(delayMinutes: 15);

        var messages = new[] { cancelled, terminated, changedPlatform, delayed }
            .Select(journey => Assert.IsType<PublicGuidance>(Build(journey, journey, Next())).Message)
            .ToArray();

        Assert.All(messages, message =>
        {
            Assert.True(char.IsUpper(message[0]), $"'{message}' does not start a sentence.");
            Assert.EndsWith(".", message, StringComparison.Ordinal);
            Assert.DoesNotContain("  ", message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Every_time_is_written_on_the_railway_clock()
    {
        // 02:30 UTC is 08:00 on the railway's UTC+05:30 clock, and 02:50 UTC is 08:20.
        var utcDeparture = new DateTimeOffset(2026, 10, 28, 2, 30, 0, TimeSpan.Zero);
        var utcNextDeparture = new DateTimeOffset(2026, 10, 28, 2, 50, 0, TimeSpan.Zero);
        var utcNextExpected = new DateTimeOffset(2026, 10, 28, 2, 55, 0, TimeSpan.Zero);

        var cancelled = GuidanceFixture.Journey(
            status: JourneyLifecycleStatus.Cancelled,
            stops: CancelledStopsAt(utcDeparture));
        var next = GuidanceFixture.Candidate(utcNextDeparture, utcNextExpected);

        var guidance = Build(cancelled, cancelled, next);

        Assert.Equal(
            "The 08:00 Main Line service to Gamma is cancelled. Please take the next service. "
                + "The next service is the 08:20 to Gamma, expected at 08:25 from platform 1.",
            Assert.IsType<PublicGuidance>(guidance).Message);
    }

    private static PublicGuidance? Build(
        LiveJourneyView journey,
        params LiveJourneyView[] sameDayJourneys) =>
        GuidanceBuilder.Build(
            journey,
            sameDayJourneys,
            GuidanceFixture.Lookup(),
            GuidanceFixture.RailwayZone);

    private static LiveJourneyView Next() =>
        GuidanceFixture.Candidate(GuidanceFixture.At(8, 20), GuidanceFixture.At(8, 25));

    /// <summary>The fixture route with the platform of the named stops changed, as a published change leaves it.</summary>
    private static LiveStopView[] ChangedPlatforms(params (int Sequence, Guid Platform)[] changes) =>
        GuidanceFixture.Route(GuidanceFixture.At(8, 0), GuidanceFixture.At(8, 2))
            .Select(stop =>
            {
                var change = changes.FirstOrDefault(candidate => candidate.Sequence == stop.Sequence);
                return change.Platform == Guid.Empty
                    ? stop
                    : GuidanceFixture.WithPlatformChange(stop, change.Platform, GuidanceFixture.At(7, 41));
            })
            .ToArray();

    private static LiveStopView[] CancelledStopsAt(DateTimeOffset departure) =>
        GuidanceFixture.CancelledStops()
            .Select(stop => stop.Sequence == 1
                ? stop with { ScheduledDeparture = departure, ExpectedDeparture = null }
                : stop.Sequence == 2
                    ? stop with
                    {
                        ScheduledDeparture = departure.AddMinutes(10),
                        ExpectedDeparture = null,
                    }
                    : stop with
                    {
                        ScheduledArrival = departure.AddMinutes(20),
                        ExpectedArrival = null,
                    })
            .ToArray();
}
