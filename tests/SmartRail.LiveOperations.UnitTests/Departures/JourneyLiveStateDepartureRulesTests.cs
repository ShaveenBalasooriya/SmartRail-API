using SmartRail.LiveOperations.Domain.Calculation;
using SmartRail.LiveOperations.Domain.Changes;
using SmartRail.LiveOperations.Domain.Departures;
using SmartRail.LiveOperations.Domain.LiveState;

namespace SmartRail.LiveOperations.UnitTests.Departures;

/// <summary>
/// The departure rules that need the journey's live state (departure-logging spec "Only valid
/// stops" and "Journeys open for logging"): a departure may be logged only at a calling stop that is
/// not the terminus, and logging one on a journey a controller already cancelled or terminated is
/// recorded without changing that status.
/// </summary>
public sealed class JourneyLiveStateDepartureRulesTests
{
    private static readonly DateTimeOffset CreatedAt = At(7, 0);

    private static readonly Guid PlatformOne = Guid.CreateVersion7();

    private static readonly Guid PlatformTwo = Guid.CreateVersion7();

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void A_departure_may_be_logged_at_a_calling_stop_that_is_not_the_terminus(int stopSequence)
    {
        var state = ScheduledJourney();

        Assert.True(state.CheckDepartureCanBeLogged(stopSequence).IsSuccess);
    }

    [Fact]
    public void A_departure_may_not_be_logged_at_a_stop_the_journey_passes_without_calling()
    {
        var state = ScheduledJourney();

        var result = state.CheckDepartureCanBeLogged(3);

        Assert.True(result.IsFailure);
        Assert.Equal("DepartureLog.StopNotCalling", result.Error.Code);
        Assert.Contains("3", result.Error.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void A_departure_may_not_be_logged_at_a_stop_the_journey_does_not_have()
    {
        var state = ScheduledJourney();

        var result = state.CheckDepartureCanBeLogged(99);

        Assert.True(result.IsFailure);
        Assert.Equal("DepartureLog.StopNotFound", result.Error.Code);
        Assert.Contains("99", result.Error.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void A_departure_may_not_be_logged_at_the_journeys_final_calling_stop()
    {
        var state = ScheduledJourney();

        var result = state.CheckDepartureCanBeLogged(4);

        Assert.True(result.IsFailure);
        Assert.Equal(DepartureLogErrors.StopIsFinalStop, result.Error);
    }

    [Fact]
    public void A_departure_logged_after_a_cancellation_is_recorded_and_the_journey_stays_cancelled()
    {
        var state = ScheduledJourney();
        var cancelledAt = At(7, 30);
        var cancellation = new ChangeFact(
            ChangeKind.Cancellation,
            StopSequence: null,
            DelayMinutes: null,
            PlatformId: null,
            cancelledAt,
            StreamOrder: 1,
            Corrected: false);

        Assert.True(state.Apply([cancellation], cancelledAt));
        Assert.Equal(JourneyLifecycleStatus.Cancelled, state.Status);

        var departedAt = At(8, 2);

        Assert.True(state.Apply(
            [cancellation, new DepartureFact(1, departedAt, departedAt, StreamOrder: 2)],
            departedAt));

        Assert.Equal(JourneyLifecycleStatus.Cancelled, state.Status);
        Assert.Equal(departedAt, state.StopAt(1)!.ActualDeparture);
    }

    [Fact]
    public void A_departure_logged_after_a_termination_is_recorded_and_the_journey_stays_terminated()
    {
        var state = ScheduledJourney();
        var terminatedAt = At(8, 15);
        var termination = new ChangeFact(
            ChangeKind.Termination,
            StopSequence: 2,
            DelayMinutes: null,
            PlatformId: null,
            terminatedAt,
            StreamOrder: 1,
            Corrected: false);

        Assert.True(state.Apply([termination], terminatedAt));
        Assert.Equal(JourneyLifecycleStatus.Terminated, state.Status);

        var departedAt = At(8, 2);

        Assert.True(state.Apply(
            [termination, new DepartureFact(1, departedAt, departedAt, StreamOrder: 2)],
            departedAt));

        Assert.Equal(JourneyLifecycleStatus.Terminated, state.Status);
        Assert.Equal(departedAt, state.StopAt(1)!.ActualDeparture);
    }

    private static JourneyLiveState ScheduledJourney()
    {
        var baseline = new Baseline(
            Guid.CreateVersion7(),
            new DateOnly(2026, 10, 28),
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
        new(2026, 10, 28, hour, minute, 0, TimeSpan.Zero);
}
