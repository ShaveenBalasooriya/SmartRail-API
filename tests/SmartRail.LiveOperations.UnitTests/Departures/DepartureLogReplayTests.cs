using SmartRail.LiveOperations.Domain.Departures;

namespace SmartRail.LiveOperations.UnitTests.Departures;

/// <summary>
/// The rule that decides whether a resend of a client identifier is a replay or the same identifier
/// reused with different content (departure-logging design D3). It is pure, so the microsecond
/// comparison PostgreSQL forces can be checked without a database.
/// </summary>
public sealed class DepartureLogReplayTests
{
    private static readonly DateTimeOffset DepartedAt = new(2026, 10, 28, 8, 2, 0, TimeSpan.Zero);

    private readonly Guid _journeyId = Guid.CreateVersion7();

    [Fact]
    public void Identical_content_is_a_replay()
    {
        var log = Create(DepartedAt.AddTicks(-3), DelayCause.Crew);

        Assert.True(log.MatchesReplay(_journeyId, stopSequence: 2, DepartedAt.AddTicks(-3), DelayCause.Crew));
    }

    [Fact]
    public void A_difference_below_the_microsecond_still_matches_because_postgresql_drops_it()
    {
        var microseconds = DepartedAt.AddTicks(1_234_560);
        var log = Create(microseconds, DelayCause.Crew);

        Assert.True(log.MatchesReplay(
            _journeyId,
            stopSequence: 2,
            microseconds.AddTicks(7),
            DelayCause.Crew));
    }

    [Fact]
    public void The_same_instant_in_another_offset_is_a_replay()
    {
        var log = Create(DepartedAt, DelayCause.Crew);

        Assert.True(log.MatchesReplay(
            _journeyId,
            stopSequence: 2,
            DepartedAt.ToOffset(TimeSpan.FromHours(5.5)),
            DelayCause.Crew));
    }

    [Fact]
    public void A_different_journey_is_not_a_replay()
    {
        var log = Create(DepartedAt, DelayCause.Crew);

        Assert.False(log.MatchesReplay(Guid.CreateVersion7(), stopSequence: 2, DepartedAt, DelayCause.Crew));
    }

    [Fact]
    public void A_different_stop_is_not_a_replay()
    {
        var log = Create(DepartedAt, DelayCause.Crew);

        Assert.False(log.MatchesReplay(_journeyId, stopSequence: 3, DepartedAt, DelayCause.Crew));
    }

    [Fact]
    public void A_different_device_time_is_not_a_replay()
    {
        var log = Create(DepartedAt, DelayCause.Crew);

        Assert.False(log.MatchesReplay(
            _journeyId,
            stopSequence: 2,
            DepartedAt.AddMilliseconds(1),
            DelayCause.Crew));
    }

    [Fact]
    public void A_different_delay_cause_is_not_a_replay()
    {
        var log = Create(DepartedAt, DelayCause.Crew);

        Assert.False(log.MatchesReplay(_journeyId, stopSequence: 2, DepartedAt, DelayCause.Weather));
    }

    [Fact]
    public void Dropping_or_adding_the_cause_is_not_a_replay()
    {
        var withoutCause = Create(DepartedAt, delayCause: null);
        var withCause = Create(DepartedAt, DelayCause.Crew);

        Assert.False(withoutCause.MatchesReplay(_journeyId, stopSequence: 2, DepartedAt, DelayCause.Crew));
        Assert.False(withCause.MatchesReplay(_journeyId, stopSequence: 2, DepartedAt, delayCause: null));
    }

    private DepartureLog Create(DateTimeOffset departedAt, DelayCause? delayCause)
    {
        var created = DepartureLog.Create(
            Guid.CreateVersion7(),
            _journeyId,
            stopSequence: 2,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            departedAt,
            delayCause,
            DepartedAt.AddMinutes(20));

        Assert.True(created.IsSuccess);
        return created.Value;
    }
}
