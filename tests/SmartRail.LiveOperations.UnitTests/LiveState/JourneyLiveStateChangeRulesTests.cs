using SmartRail.LiveOperations.Domain.Calculation;
using SmartRail.LiveOperations.Domain.Changes;
using SmartRail.LiveOperations.Domain.LiveState;

namespace SmartRail.LiveOperations.UnitTests.LiveState;

/// <summary>
/// The rules a published change is checked against before anything is written (status-publication
/// spec): only today's scheduled or running journey is open, a delay may start only at a calling stop
/// that has not departed, only a journey with no recorded departure may be cancelled, an early
/// termination may end only at a calling stop that is not the last one and not before the last
/// departure, and a platform change may move only a calling stop that has not departed to another
/// platform. The range of the delay and the shape of each change itself are rules of
/// <see cref="StatusChange"/> and are covered by <c>StatusChangeFactoryTests</c>.
/// </summary>
public sealed class JourneyLiveStateChangeRulesTests
{
    private static readonly DateTimeOffset CreatedAt = At(7, 0);

    private static readonly Guid PlatformOne = Guid.CreateVersion7();

    private static readonly Guid PlatformTwo = Guid.CreateVersion7();

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void A_delay_may_start_at_a_calling_stop_that_has_not_departed(int stopSequence)
    {
        var state = ScheduledJourney();

        Assert.True(state.CheckDelayCanStart(stopSequence).IsSuccess);
    }

    [Fact]
    public void A_delay_may_not_start_at_a_stop_the_journey_passes_without_calling()
    {
        var state = ScheduledJourney();

        var result = state.CheckDelayCanStart(3);

        Assert.True(result.IsFailure);
        Assert.Equal(StatusChangeErrors.DelayStopNotCalling, result.Error);
    }

    [Fact]
    public void A_delay_may_not_start_at_a_stop_the_journey_does_not_have()
    {
        var state = ScheduledJourney();

        var result = state.CheckDelayCanStart(99);

        Assert.True(result.IsFailure);
        Assert.Equal(StatusChangeErrors.DelayStopNotCalling, result.Error);
    }

    [Fact]
    public void A_delay_may_not_start_at_a_departed_stop()
    {
        var state = DepartedJourney();

        var result = state.CheckDelayCanStart(1);

        Assert.True(result.IsFailure);
        Assert.Equal(StatusChangeErrors.DelayStopAlreadyDeparted, result.Error);
    }

    [Fact]
    public void The_delay_a_stop_shows_before_a_change_is_zero_without_a_previous_change()
    {
        var state = ScheduledJourney();

        Assert.Equal(0, state.DelayMinutesAt(2));
    }

    [Fact]
    public void The_delay_a_stop_shows_before_a_change_is_the_published_delay()
    {
        var state = ScheduledJourney();
        var published = At(7, 30);

        Assert.True(state.Apply(
            [new ChangeFact(ChangeKind.Delay, 2, 15, null, published, StreamOrder: 1, Corrected: false)],
            published));

        Assert.Equal(0, state.DelayMinutesAt(1));
        Assert.Equal(15, state.DelayMinutesAt(2));
        Assert.Equal(15, state.DelayMinutesAt(4));
    }

    [Fact]
    public void A_scheduled_journey_is_open_for_changes()
    {
        Assert.True(ScheduledJourney().IsOpenForChanges);
    }

    [Fact]
    public void A_journey_with_no_recorded_departure_may_be_cancelled()
    {
        var state = ScheduledJourney();

        Assert.False(state.HasRecordedDeparture);
        Assert.True(state.CheckCanBeCancelled().IsSuccess);
    }

    [Fact]
    public void A_journey_with_a_recorded_departure_may_not_be_cancelled()
    {
        var state = DepartedJourney();

        var result = state.CheckCanBeCancelled();

        Assert.True(state.HasRecordedDeparture);
        Assert.True(result.IsFailure);
        Assert.Equal(StatusChangeErrors.CancellationAfterDeparture, result.Error);
    }

    [Fact]
    public void A_platform_change_may_move_a_calling_stop_that_has_not_departed()
    {
        var state = ScheduledJourney();

        Assert.True(state.CheckPlatformCanChange(1, PlatformTwo).IsSuccess);
    }

    [Fact]
    public void A_platform_change_may_not_move_a_stop_the_journey_passes_without_calling()
    {
        var state = ScheduledJourney();

        var result = state.CheckPlatformCanChange(3, PlatformTwo);

        Assert.True(result.IsFailure);
        Assert.Equal(StatusChangeErrors.PlatformStopNotCalling, result.Error);
    }

    [Fact]
    public void A_platform_change_may_not_move_a_stop_the_journey_does_not_have()
    {
        var state = ScheduledJourney();

        var result = state.CheckPlatformCanChange(99, PlatformTwo);

        Assert.True(result.IsFailure);
        Assert.Equal(StatusChangeErrors.PlatformStopNotCalling, result.Error);
    }

    [Fact]
    public void A_platform_change_may_not_move_a_departed_stop()
    {
        var state = DepartedJourney();

        var result = state.CheckPlatformCanChange(1, PlatformTwo);

        Assert.True(result.IsFailure);
        Assert.Equal(StatusChangeErrors.PlatformStopAlreadyDeparted, result.Error);
    }

    [Fact]
    public void A_platform_change_may_not_keep_the_stop_on_its_platform()
    {
        var state = ScheduledJourney();

        var result = state.CheckPlatformCanChange(1, PlatformOne);

        Assert.True(result.IsFailure);
        Assert.Equal(StatusChangeErrors.PlatformUnchanged, result.Error);
    }

    [Fact]
    public void A_running_journey_is_open_for_changes()
    {
        Assert.True(DepartedJourney().IsOpenForChanges);
    }

    [Fact]
    public void A_running_journey_may_be_terminated_at_a_calling_stop_before_its_last()
    {
        var state = DepartedJourney();

        Assert.True(state.CheckCanBeTerminated(2).IsSuccess);
    }

    [Fact]
    public void A_scheduled_journey_may_not_be_terminated()
    {
        var state = ScheduledJourney();

        var result = state.CheckCanBeTerminated(2);

        Assert.True(result.IsFailure);
        Assert.Equal(StatusChangeErrors.TerminationNotStarted, result.Error);
        Assert.Contains("cancel it instead", result.Error.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void A_termination_may_not_end_at_a_stop_the_journey_passes_without_calling()
    {
        var state = DepartedJourney();

        var result = state.CheckCanBeTerminated(3);

        Assert.True(result.IsFailure);
        Assert.Equal(StatusChangeErrors.TerminationStopNotCalling, result.Error);
    }

    [Fact]
    public void A_termination_may_not_end_at_a_stop_the_journey_does_not_have()
    {
        var state = DepartedJourney();

        var result = state.CheckCanBeTerminated(99);

        Assert.True(result.IsFailure);
        Assert.Equal(StatusChangeErrors.TerminationStopNotCalling, result.Error);
    }

    [Fact]
    public void A_termination_may_not_end_at_the_final_calling_stop()
    {
        var state = DepartedJourney();

        var result = state.CheckCanBeTerminated(4);

        Assert.True(result.IsFailure);
        Assert.Equal(StatusChangeErrors.TerminationAtFinalStop, result.Error);
    }

    [Fact]
    public void A_termination_may_not_end_before_the_last_recorded_departure()
    {
        var state = JourneyDepartedAtStopsOneAndTwo();

        var result = state.CheckCanBeTerminated(1);

        Assert.True(result.IsFailure);
        Assert.Equal(StatusChangeErrors.TerminationBeforeLastDeparture, result.Error);
    }

    [Fact]
    public void A_termination_may_end_at_the_last_recorded_departure()
    {
        var state = JourneyDepartedAtStopsOneAndTwo();

        Assert.True(state.CheckCanBeTerminated(2).IsSuccess);
    }

    [Fact]
    public void A_terminated_journey_is_closed_to_further_terminations()
    {
        var state = ScheduledJourney();
        var departure = At(8, 1);
        var published = At(8, 5);

        Assert.True(state.Apply(
            [
                new DepartureFact(1, departure, departure, StreamOrder: 1),
                new ChangeFact(ChangeKind.Termination, 2, null, null, published, StreamOrder: 2, Corrected: false),
            ],
            published));

        Assert.Equal(JourneyLifecycleStatus.Terminated, state.Status);
        Assert.False(state.IsOpenForChanges);
        Assert.Equal(StatusChangeErrors.JourneyClosed, state.CheckCanBeTerminated(2).Error);
    }

    [Fact]
    public void A_cancelled_journey_is_closed_to_changes()
    {
        var state = ScheduledJourney();
        var published = At(7, 30);

        Assert.True(state.Apply(
            [new ChangeFact(ChangeKind.Cancellation, null, null, null, published, StreamOrder: 1, Corrected: false)],
            published));

        Assert.Equal(JourneyLifecycleStatus.Cancelled, state.Status);
        Assert.False(state.IsOpenForChanges);
    }

    private static JourneyLiveState DepartedJourney()
    {
        var state = ScheduledJourney();
        var departure = At(8, 1);

        Assert.True(state.Apply([new DepartureFact(1, departure, departure, StreamOrder: 1)], departure));

        Assert.Equal(JourneyLifecycleStatus.Running, state.Status);
        return state;
    }

    /// <summary>A running journey whose stops 1 and 2 have both departed, so stop 2 is the last departure.</summary>
    private static JourneyLiveState JourneyDepartedAtStopsOneAndTwo()
    {
        var state = ScheduledJourney();
        var first = At(8, 1);
        var second = At(8, 11);

        Assert.True(state.Apply(
            [
                new DepartureFact(1, first, first, StreamOrder: 1),
                new DepartureFact(2, second, second, StreamOrder: 2),
            ],
            second));

        Assert.Equal(JourneyLifecycleStatus.Running, state.Status);
        return state;
    }

    private static JourneyLiveState ScheduledJourney()
    {
        var baseline = new Baseline(
            Guid.CreateVersion7(),
            new DateOnly(2026, 10, 22),
            Guid.CreateVersion7(),
            [
                new BaselineStop(1, Guid.CreateVersion7(), true, null, At(8, 0), PlatformOne),
                new BaselineStop(2, Guid.CreateVersion7(), true, At(8, 10), At(8, 11), PlatformTwo),
                new BaselineStop(3, Guid.CreateVersion7(), false, null, null, null),
                new BaselineStop(4, Guid.CreateVersion7(), true, At(8, 20), null, null),
            ],
            CreatedAt);

        return JourneyLiveState.Create(baseline, Guid.CreateVersion7(), Guid.CreateVersion7(), CreatedAt).Value;
    }

    private static DateTimeOffset At(int hour, int minute) =>
        new(2026, 10, 22, hour, minute, 0, TimeSpan.Zero);
}
