using SmartRail.LiveOperations.Domain.Calculation;
using SmartRail.LiveOperations.Domain.LiveState;

namespace SmartRail.LiveOperations.UnitTests.LiveState;

/// <summary>
/// DEF-007: the stored stop holds the platform the latest applied change left behind, so a
/// recomputation after a correction recovers the timetable's platform from the earliest platform
/// change's previous platform. Each Apply call receives every stored fact, as the fact reader does.
/// </summary>
public sealed class JourneyLiveStateCorrectedPlatformTests
{
    private static readonly DateTimeOffset CreatedAt = At(7, 0);

    private static readonly Guid PlatformOne = Guid.CreateVersion7();

    private static readonly Guid PlatformTwo = Guid.CreateVersion7();

    private static readonly Guid PlatformThree = Guid.CreateVersion7();

    [Fact]
    public void Happy_path_withdrawing_the_only_platform_change_restores_the_timetable_platform()
    {
        // Arrange
        var state = ScheduledJourney();
        Assert.True(state.Apply([PlatformChange(PlatformTwo, PlatformOne, order: 1)], At(7, 30)));
        Assert.Equal(PlatformTwo, state.StopAt(1)!.PlatformId);

        // Act
        var changed = state.Apply([PlatformChange(PlatformTwo, PlatformOne, order: 1, corrected: true)], At(7, 40));

        // Assert
        Assert.True(changed);
        Assert.Equal(PlatformOne, state.StopAt(1)!.PlatformId);
    }

    [Fact]
    public void Withdrawing_the_first_of_two_platform_changes_keeps_the_later_one()
    {
        // Arrange
        var state = ScheduledJourney();
        var first = PlatformChange(PlatformTwo, PlatformOne, order: 1);
        var second = PlatformChange(PlatformThree, PlatformTwo, order: 2);
        state.Apply([first, second], At(7, 35));

        // Act
        state.Apply([first with { Corrected = true }, second], At(7, 40));

        // Assert
        Assert.Equal(PlatformThree, state.StopAt(1)!.PlatformId);
    }

    [Fact]
    public void Withdrawing_both_platform_changes_returns_to_the_timetable_platform()
    {
        // Arrange
        var state = ScheduledJourney();
        var first = PlatformChange(PlatformTwo, PlatformOne, order: 1);
        var second = PlatformChange(PlatformThree, PlatformTwo, order: 2);
        state.Apply([first, second], At(7, 35));

        // Act
        state.Apply([first with { Corrected = true }, second with { Corrected = true }], At(7, 40));

        // Assert
        Assert.Equal(PlatformOne, state.StopAt(1)!.PlatformId);
    }

    [Fact]
    public void A_platform_change_at_one_stop_leaves_the_other_stops_on_their_own_platform()
    {
        // Arrange
        var state = ScheduledJourney();
        state.Apply([PlatformChange(PlatformThree, PlatformOne, order: 1)], At(7, 30));

        // Act
        state.Apply([PlatformChange(PlatformThree, PlatformOne, order: 1, corrected: true)], At(7, 40));

        // Assert
        Assert.Equal(PlatformOne, state.StopAt(1)!.PlatformId);
        Assert.Equal(PlatformTwo, state.StopAt(2)!.PlatformId);
    }

    [Fact]
    public void A_change_without_a_previous_platform_falls_back_to_the_stored_platform()
    {
        // Known limit of the fix: with no previous platform recorded there is nothing to recover from,
        // so the stored (already changed) platform stays.

        // Arrange
        var state = ScheduledJourney();
        state.Apply([PlatformChange(PlatformTwo, previous: null, order: 1)], At(7, 30));

        // Act
        state.Apply([PlatformChange(PlatformTwo, previous: null, order: 1, corrected: true)], At(7, 40));

        // Assert
        Assert.Equal(PlatformTwo, state.StopAt(1)!.PlatformId);
    }

    private static ChangeFact PlatformChange(Guid platform, Guid? previous, long order, bool corrected = false) =>
        new(ChangeKind.PlatformChange, 1, null, platform, At(7, 20 + (int)order), order, corrected, previous);

    private static JourneyLiveState ScheduledJourney()
    {
        var baseline = new Baseline(
            Guid.CreateVersion7(),
            new DateOnly(2026, 10, 22),
            Guid.CreateVersion7(),
            [
                new BaselineStop(1, Guid.CreateVersion7(), true, null, At(8, 0), PlatformOne),
                new BaselineStop(2, Guid.CreateVersion7(), true, At(8, 10), At(8, 11), PlatformTwo),
            ],
            CreatedAt);

        return JourneyLiveState.Create(baseline, Guid.CreateVersion7(), Guid.CreateVersion7(), CreatedAt).Value;
    }

    private static DateTimeOffset At(int hour, int minute) =>
        new(2026, 10, 22, hour, minute, 0, TimeSpan.Zero);
}
