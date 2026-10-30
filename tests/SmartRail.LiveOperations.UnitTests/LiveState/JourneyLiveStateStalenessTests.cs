using SmartRail.LiveOperations.Domain.Calculation;
using SmartRail.LiveOperations.Domain.LiveState;

namespace SmartRail.LiveOperations.UnitTests.LiveState;

/// <summary>
/// The stale-flag rule (NC-02): only a running journey can be stale, and only when its freshness time
/// is strictly older than the threshold. The times are relative to the state's own last-updated time,
/// so the rule is tested without depending on how the calculator derives that time.
/// </summary>
public sealed class JourneyLiveStateStalenessTests
{
    private static readonly DateTimeOffset CreatedAt = At(7, 0);

    [Theory]
    [InlineData(900, 10, true)]
    [InlineData(120, 10, false)]
    [InlineData(600, 10, false)]
    [InlineData(601, 10, true)]
    [InlineData(179, 3, false)]
    [InlineData(181, 3, true)]
    public void A_running_journey_is_stale_only_when_older_than_the_threshold(
        int secondsSinceUpdate,
        int thresholdMinutes,
        bool expectedStale)
    {
        var state = RunningJourney();

        var now = state.LastUpdatedAt.AddSeconds(secondsSinceUpdate);

        Assert.Equal(expectedStale, state.IsStaleAt(now, TimeSpan.FromMinutes(thresholdMinutes)));
    }

    [Fact]
    public void A_running_journey_one_tick_past_the_threshold_is_stale()
    {
        var state = RunningJourney();
        var threshold = TimeSpan.FromMinutes(10);

        Assert.False(state.IsStaleAt(state.LastUpdatedAt + threshold, threshold));
        Assert.True(state.IsStaleAt(state.LastUpdatedAt + threshold + TimeSpan.FromTicks(1), threshold));
    }

    [Fact]
    public void A_scheduled_journey_is_never_stale()
    {
        var state = ScheduledJourney();

        Assert.Equal(JourneyLifecycleStatus.Scheduled, state.Status);
        Assert.False(state.IsStaleAt(state.LastUpdatedAt.AddHours(3), TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void A_cancelled_journey_is_never_stale()
    {
        var state = CancelledJourney();

        Assert.Equal(JourneyLifecycleStatus.Cancelled, state.Status);
        Assert.False(state.IsStaleAt(state.LastUpdatedAt.AddHours(3), TimeSpan.FromMinutes(10)));
    }

    private static JourneyLiveState RunningJourney()
    {
        var state = ScheduledJourney();

        // The first calling stop has departed, and the terminus has not yet been reached, so the
        // journey is running.
        var departure = At(8, 1);
        Assert.True(state.Apply([new DepartureFact(1, departure, departure, StreamOrder: 1)], departure));

        Assert.Equal(JourneyLifecycleStatus.Running, state.Status);
        return state;
    }

    private static JourneyLiveState CancelledJourney()
    {
        var state = ScheduledJourney();

        var published = At(7, 30);
        Assert.True(state.Apply(
            [new ChangeFact(ChangeKind.Cancellation, null, null, null, published, StreamOrder: 1, Corrected: false)],
            published));

        return state;
    }

    private static JourneyLiveState ScheduledJourney()
    {
        var baseline = new Baseline(
            Guid.CreateVersion7(),
            new DateOnly(2026, 10, 22),
            Guid.CreateVersion7(),
            [
                new BaselineStop(1, Guid.CreateVersion7(), true, null, At(8, 0), null),
                new BaselineStop(2, Guid.CreateVersion7(), true, At(8, 10), At(8, 11), null),
                new BaselineStop(3, Guid.CreateVersion7(), true, At(8, 20), null, null),
            ],
            CreatedAt);

        return JourneyLiveState.Create(baseline, Guid.CreateVersion7(), Guid.CreateVersion7(), CreatedAt).Value;
    }

    private static DateTimeOffset At(int hour, int minute) =>
        new(2026, 10, 22, hour, minute, 0, TimeSpan.Zero);
}
