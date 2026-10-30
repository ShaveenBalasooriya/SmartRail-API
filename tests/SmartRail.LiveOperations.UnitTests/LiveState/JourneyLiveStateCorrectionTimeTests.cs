using SmartRail.LiveOperations.Domain.Calculation;
using SmartRail.LiveOperations.Domain.LiveState;

namespace SmartRail.LiveOperations.UnitTests.LiveState;

/// <summary>
/// The journey's correction time (package LD0, decision D-10): the publish time of the most recent
/// correction applied to the journey, held by the live state so the public view and the journey event
/// snapshot can report it. It is null until the first correction and only ever moves forward.
/// </summary>
public sealed class JourneyLiveStateCorrectionTimeTests
{
    private static readonly DateTimeOffset CreatedAt = At(7, 0);

    [Fact]
    public void A_journey_that_was_never_corrected_has_no_correction_time()
    {
        Assert.Null(ScheduledJourney().CorrectedAt);
    }

    [Fact]
    public void The_first_correction_sets_the_correction_time()
    {
        var state = ScheduledJourney();
        var correctedAt = At(8, 10);

        Assert.True(state.MarkCorrected(correctedAt));

        Assert.Equal(correctedAt, state.CorrectedAt);
    }

    [Fact]
    public void An_older_correction_does_not_move_the_correction_time_back()
    {
        var state = ScheduledJourney();
        var latest = At(8, 10);

        Assert.True(state.MarkCorrected(latest));
        Assert.False(state.MarkCorrected(At(8, 5)));

        Assert.Equal(latest, state.CorrectedAt);
    }

    [Fact]
    public void A_later_correction_moves_the_correction_time_forward()
    {
        var state = ScheduledJourney();
        var later = At(8, 20);

        Assert.True(state.MarkCorrected(At(8, 10)));
        Assert.True(state.MarkCorrected(later));

        Assert.Equal(later, state.CorrectedAt);
    }

    [Fact]
    public void The_same_correction_applied_twice_changes_nothing()
    {
        var state = ScheduledJourney();
        var correctedAt = At(8, 10);

        Assert.True(state.MarkCorrected(correctedAt));
        Assert.False(state.MarkCorrected(correctedAt));

        Assert.Equal(correctedAt, state.CorrectedAt);
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
