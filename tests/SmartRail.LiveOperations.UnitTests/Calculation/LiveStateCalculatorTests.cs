using SmartRail.LiveOperations.Domain.Calculation;

namespace SmartRail.LiveOperations.UnitTests.Calculation;

/// <summary>
/// One test per scenario in <c>specs/live-running-state/spec.md</c>. Every journey below runs five
/// calling stops between 08:00 and 08:40 on one day, so the expected times stay readable.
/// </summary>
public sealed class LiveStateCalculatorTests
{
    private static readonly Guid JourneyId = Guid.CreateVersion7();
    private static readonly Guid GuardStaffId = Guid.CreateVersion7();

    private static readonly Guid Station1 = Guid.CreateVersion7();
    private static readonly Guid Station2 = Guid.CreateVersion7();
    private static readonly Guid Station3 = Guid.CreateVersion7();
    private static readonly Guid Station4 = Guid.CreateVersion7();
    private static readonly Guid Station5 = Guid.CreateVersion7();

    private static readonly Guid Platform1 = Guid.CreateVersion7();
    private static readonly Guid Platform2 = Guid.CreateVersion7();
    private static readonly Guid Platform3 = Guid.CreateVersion7();
    private static readonly Guid Platform4 = Guid.CreateVersion7();
    private static readonly Guid Platform5 = Guid.CreateVersion7();
    private static readonly Guid ReplacementPlatform = Guid.CreateVersion7();

    private static readonly DateTimeOffset CreatedAt = At(7, 0);

    [Fact]
    public void Late_departure_propagates()
    {
        Fact[] facts = [new DepartureFact(2, At(8, 20), At(8, 20), StreamOrder: 1)];

        var state = LiveStateCalculator.Compute(CreateBaseline(), facts, At(8, 25));

        Assert.Equal(8, state.DelayMinutes);
        Assert.Equal(At(8, 28), state.Stops[2].ExpectedArrival);
        Assert.Equal(At(8, 30), state.Stops[2].ExpectedDeparture);
        Assert.Equal(At(8, 48), state.Stops[4].ExpectedArrival);
    }

    [Fact]
    public void Controller_delay_after_a_guard_log()
    {
        Fact[] facts =
        [
            new DepartureFact(2, At(8, 15), At(8, 15), StreamOrder: 1),
            new ChangeFact(ChangeKind.Delay, 4, 15, null, At(8, 16), StreamOrder: 2, Corrected: false),
        ];

        var state = LiveStateCalculator.Compute(CreateBaseline(), facts, At(8, 17));

        Assert.Equal(3, state.DelayMinutes);
        Assert.Equal(At(8, 23), state.Stops[2].ExpectedArrival);
        Assert.Equal(At(8, 45), state.Stops[3].ExpectedArrival);
        Assert.Equal(At(8, 55), state.Stops[4].ExpectedArrival);
    }

    [Fact]
    public void Guard_log_after_a_controller_delay()
    {
        Fact[] facts =
        [
            new ChangeFact(ChangeKind.Delay, 3, 15, null, At(8, 15), StreamOrder: 1, Corrected: false),
            new DepartureFact(3, At(8, 32), At(8, 33), StreamOrder: 2),
        ];

        var state = LiveStateCalculator.Compute(CreateBaseline(), facts, At(8, 34));

        Assert.Equal(10, state.DelayMinutes);
        Assert.Equal(At(8, 32), state.Stops[2].ActualDeparture);
        Assert.Equal(At(8, 40), state.Stops[3].ExpectedArrival);
        Assert.Equal(At(8, 42), state.Stops[3].ExpectedDeparture);
    }

    [Fact]
    public void Two_minutes_early()
    {
        Fact[] facts = [new DepartureFact(1, At(7, 58), At(7, 58), StreamOrder: 1)];

        var state = LiveStateCalculator.Compute(CreateBaseline(), facts, At(7, 59));

        Assert.Equal(-2, state.DelayMinutes);
        Assert.Equal(At(8, 8), state.Stops[1].ExpectedArrival);
        Assert.Equal(At(8, 12), state.Stops[1].ExpectedDeparture);
    }

    [Fact]
    public void Mid_journey()
    {
        Fact[] facts =
        [
            new DepartureFact(1, At(8, 0), At(8, 0), StreamOrder: 1),
            new DepartureFact(2, At(8, 12), At(8, 12), StreamOrder: 2),
        ];

        var state = LiveStateCalculator.Compute(CreateBaseline(), facts, At(8, 13));

        Assert.Equal(JourneyStatus.Running, state.Status);
        Assert.Equal(Station2, state.LastDepartedStationId);
        Assert.Equal(Station3, state.NextStationId);
    }

    [Fact]
    public void Departures_recorded_out_of_order()
    {
        Fact[] facts =
        [
            new DepartureFact(3, At(8, 22), At(8, 22), StreamOrder: 1),
            new DepartureFact(2, At(8, 12), At(8, 13), StreamOrder: 2),
        ];

        var state = LiveStateCalculator.Compute(CreateBaseline(), facts, At(8, 23));

        Assert.Equal(Station3, state.LastDepartedStationId);
        Assert.Equal(Station4, state.NextStationId);
        Assert.Equal(At(8, 12), state.Stops[1].ActualDeparture);
        Assert.Equal(At(8, 22), state.Stops[2].ActualDeparture);
    }

    [Fact]
    public void Platform_changed_then_corrected()
    {
        var baseline = CreateBaseline();
        var change = new ChangeFact(
            ChangeKind.PlatformChange,
            2,
            null,
            ReplacementPlatform,
            At(8, 5),
            StreamOrder: 1,
            Corrected: false);

        var live = LiveStateCalculator.Compute(baseline, [change], At(8, 6));

        Assert.Equal(ReplacementPlatform, live.Stops[1].PlatformId);
        Assert.Equal(At(8, 5), live.Stops[1].PlatformChangedAt);
        Assert.Equal(Platform1, live.Stops[0].PlatformId);
        Assert.Null(live.Stops[0].PlatformChangedAt);

        var reverted = LiveStateCalculator.Compute(baseline, [change with { Corrected = true }], At(8, 6));

        Assert.Equal(Platform2, reverted.Stops[1].PlatformId);
        Assert.Null(reverted.Stops[1].PlatformChangedAt);
    }

    [Fact]
    public void First_departure()
    {
        var baseline = CreateBaseline();

        Assert.Equal(
            JourneyStatus.Scheduled,
            LiveStateCalculator.Compute(baseline, [], At(7, 59)).Status);

        Fact[] facts = [new DepartureFact(1, At(8, 0), At(8, 1), StreamOrder: 1)];

        var state = LiveStateCalculator.Compute(baseline, facts, At(8, 1));

        Assert.Equal(JourneyStatus.Running, state.Status);
    }

    [Fact]
    public void Arrival_time_passes()
    {
        Fact[] facts = [new DepartureFact(4, At(8, 32), At(8, 32), StreamOrder: 1)];
        var baseline = CreateBaseline();

        var beforeArrival = LiveStateCalculator.Compute(baseline, facts, At(8, 39));
        var atArrival = LiveStateCalculator.Compute(baseline, facts, At(8, 40));

        Assert.Equal(JourneyStatus.Running, beforeArrival.Status);
        Assert.Equal(Station5, beforeArrival.NextStationId);
        Assert.Equal(JourneyStatus.Completed, atArrival.Status);
    }

    [Fact]
    public void Never_departed()
    {
        var state = LiveStateCalculator.Compute(CreateBaseline(), [], At(10, 0));

        Assert.Equal(JourneyStatus.Scheduled, state.Status);
        Assert.Equal(CreatedAt, state.LastUpdatedAt);
        Assert.Equal(0, state.DelayMinutes);
        Assert.Equal(Station1, state.NextStationId);
        Assert.Equal(At(8, 10), state.Stops[1].ExpectedArrival);
    }

    [Fact]
    public void Cancelled_before_departure()
    {
        Fact[] facts =
        [
            new ChangeFact(ChangeKind.Cancellation, null, null, null, At(7, 30), StreamOrder: 1, Corrected: false),
        ];

        var state = LiveStateCalculator.Compute(CreateBaseline(), facts, At(7, 45));

        Assert.Equal(JourneyStatus.Cancelled, state.Status);
        Assert.Null(state.DelayMinutes);
        Assert.Null(state.NextStationId);
        Assert.Equal(At(7, 30), state.LastUpdatedAt);
        Assert.Equal(At(8, 20), state.Stops[2].ScheduledArrival);
        Assert.Equal(At(8, 22), state.Stops[2].ScheduledDeparture);
        Assert.All(state.Stops, stop =>
        {
            Assert.Null(stop.ExpectedArrival);
            Assert.Null(stop.ExpectedDeparture);
        });
    }

    [Fact]
    public void Correction_removes_an_effect()
    {
        var baseline = CreateBaseline();
        var cancellation = new ChangeFact(
            ChangeKind.Cancellation,
            null,
            null,
            null,
            At(7, 30),
            StreamOrder: 1,
            Corrected: false);

        Assert.Equal(
            JourneyStatus.Cancelled,
            LiveStateCalculator.Compute(baseline, [cancellation], At(7, 45)).Status);

        var corrected = cancellation with { Corrected = true };

        Assert.Equal(
            Shape(LiveStateCalculator.Compute(baseline, [], At(7, 45))),
            Shape(LiveStateCalculator.Compute(baseline, [corrected], At(7, 45))));
    }

    [Fact]
    public void Terminated_at_stop_three_reports_no_expected_times_after_it()
    {
        Fact[] facts =
        [
            new DepartureFact(2, At(8, 12), At(8, 12), StreamOrder: 1),
            new DepartureFact(3, At(8, 20), At(8, 20), StreamOrder: 2),
            new ChangeFact(ChangeKind.Termination, 3, null, null, At(8, 21), StreamOrder: 3, Corrected: false),
        ];

        var state = LiveStateCalculator.Compute(CreateBaseline(), facts, At(8, 25));

        Assert.Equal(JourneyStatus.Terminated, state.Status);
        Assert.Null(state.DelayMinutes);
        Assert.Null(state.NextStationId);
        Assert.Equal(Station3, state.LastDepartedStationId);

        // The stops the train served keep the actuals and expected times the departures give.
        Assert.Equal(At(8, 12), state.Stops[1].ActualDeparture);
        Assert.Equal(At(8, 12), state.Stops[1].ExpectedDeparture);
        Assert.Equal(At(8, 20), state.Stops[2].ActualDeparture);
        Assert.Equal(At(8, 22), state.Stops[2].ExpectedDeparture);

        // The stops it never reached keep their scheduled times and report no expected times.
        Assert.Equal(At(8, 30), state.Stops[3].ScheduledArrival);
        Assert.Equal(At(8, 32), state.Stops[3].ScheduledDeparture);
        Assert.Null(state.Stops[3].ExpectedArrival);
        Assert.Null(state.Stops[3].ExpectedDeparture);
        Assert.Equal(At(8, 40), state.Stops[4].ScheduledArrival);
        Assert.Null(state.Stops[4].ExpectedArrival);
        Assert.Null(state.Stops[4].ExpectedDeparture);
    }

    [Fact]
    public void Corrected_termination_returns_the_journey_to_running()
    {
        var baseline = CreateBaseline();
        Fact[] departures =
        [
            new DepartureFact(1, At(8, 0), At(8, 0), StreamOrder: 1),
            new DepartureFact(2, At(8, 12), At(8, 12), StreamOrder: 2),
        ];
        var termination = new ChangeFact(
            ChangeKind.Termination,
            2,
            null,
            null,
            At(8, 13),
            StreamOrder: 3,
            Corrected: false);

        var terminated = LiveStateCalculator.Compute(baseline, [.. departures, termination], At(8, 14));

        Assert.Equal(JourneyStatus.Terminated, terminated.Status);
        Assert.Null(terminated.Stops[2].ExpectedArrival);

        var corrected = LiveStateCalculator.Compute(
            baseline,
            [.. departures, termination with { Corrected = true }],
            At(8, 14));

        Assert.Equal(JourneyStatus.Running, corrected.Status);
        Assert.Equal(Station3, corrected.NextStationId);
        Assert.Equal(At(8, 20), corrected.Stops[2].ExpectedArrival);
        Assert.Equal(At(8, 22), corrected.Stops[2].ExpectedDeparture);
        Assert.Equal(
            Shape(LiveStateCalculator.Compute(baseline, departures, At(8, 14))),
            Shape(corrected));
    }

    [Fact]
    public void A_delay_published_before_a_termination_does_not_resurrect_later_stops()
    {
        Fact[] facts =
        [
            new DepartureFact(1, At(8, 0), At(8, 0), StreamOrder: 1),
            new ChangeFact(ChangeKind.Delay, 2, 15, null, At(8, 1), StreamOrder: 2, Corrected: false),
            new ChangeFact(ChangeKind.Termination, 3, null, null, At(8, 20), StreamOrder: 3, Corrected: false),
        ];

        var state = LiveStateCalculator.Compute(CreateBaseline(), facts, At(8, 21));

        Assert.Equal(JourneyStatus.Terminated, state.Status);

        // The stops the train served still show the published delay.
        Assert.Equal(At(8, 25), state.Stops[1].ExpectedArrival);
        Assert.Equal(At(8, 35), state.Stops[2].ExpectedArrival);
        Assert.Equal(At(8, 37), state.Stops[2].ExpectedDeparture);

        // The stops beyond the last served one show no expected time despite the delay.
        Assert.Equal(At(8, 30), state.Stops[3].ScheduledArrival);
        Assert.Null(state.Stops[3].ExpectedArrival);
        Assert.Null(state.Stops[4].ExpectedArrival);
        Assert.Null(state.Stops[4].ExpectedDeparture);
    }

    [Fact]
    public void A_corrected_change_after_a_corrected_termination_stays_withdrawn()
    {
        Fact[] facts =
        [
            new DepartureFact(1, At(8, 0), At(8, 0), StreamOrder: 1),
            new ChangeFact(ChangeKind.Termination, 3, null, null, At(8, 20), StreamOrder: 2, Corrected: true),
            new ChangeFact(ChangeKind.Delay, 4, 15, null, At(8, 21), StreamOrder: 3, Corrected: true),
        ];

        var state = LiveStateCalculator.Compute(CreateBaseline(), facts, At(8, 22));

        Assert.Equal(JourneyStatus.Running, state.Status);
        Assert.Equal(Station2, state.NextStationId);
        Assert.Equal(At(8, 30), state.Stops[3].ExpectedArrival);
        Assert.Equal(At(8, 32), state.Stops[3].ExpectedDeparture);
    }

    [Fact]
    public void Log_synced_late()
    {
        Fact[] facts = [new DepartureFact(1, At(8, 2), At(8, 20), StreamOrder: 1)];

        var state = LiveStateCalculator.Compute(CreateBaseline(), facts, At(8, 21));

        Assert.Equal(At(8, 2), state.Stops[0].ActualDeparture);
        Assert.Equal(At(8, 20), state.LastUpdatedAt);
    }

    [Fact]
    public void Guard_log_arrives()
    {
        Fact[] facts = [new DepartureFact(2, At(8, 12), At(8, 20), StreamOrder: 1)];

        var state = LiveStateCalculator.Compute(CreateBaseline(), facts, At(8, 21));

        Assert.Equal(At(8, 20), state.LastUpdatedAt);
    }

    private static Baseline CreateBaseline() => new(
        JourneyId,
        new DateOnly(2026, 10, 20),
        GuardStaffId,
        [
            new BaselineStop(1, Station1, true, null, At(8, 0), Platform1),
            new BaselineStop(2, Station2, true, At(8, 10), At(8, 12), Platform2),
            new BaselineStop(3, Station3, true, At(8, 20), At(8, 22), Platform3),
            new BaselineStop(4, Station4, true, At(8, 30), At(8, 32), Platform4),
            new BaselineStop(5, Station5, true, At(8, 40), null, Platform5),
        ],
        CreatedAt);

    private static DateTimeOffset At(int hour, int minute) =>
        new(2026, 10, 20, hour, minute, 0, TimeSpan.Zero);

    private static object?[] Shape(JourneyState state) =>
    [
        state.JourneyId,
        state.Status,
        state.DelayMinutes,
        state.LastDepartedStationId,
        state.NextStationId,
        state.LastUpdatedAt,
        .. state.Stops.SelectMany(stop => new object?[]
        {
            stop.Sequence,
            stop.ScheduledArrival,
            stop.ScheduledDeparture,
            stop.ExpectedArrival,
            stop.ExpectedDeparture,
            stop.ActualDeparture,
            stop.PlatformId,
            stop.PlatformChangedAt,
        }),
    ];
}
