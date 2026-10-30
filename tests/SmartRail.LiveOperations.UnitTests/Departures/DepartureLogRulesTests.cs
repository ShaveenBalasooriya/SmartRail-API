using SmartRail.LiveOperations.Domain.Departures;
using SmartRail.SharedKernel.Results;

namespace SmartRail.LiveOperations.UnitTests.Departures;

/// <summary>
/// The plausibility rules of a departure log (departure-logging spec "Plausible timestamps" and
/// "Journeys open for logging"): the timestamp carries its offset, it is not more than two minutes
/// ahead of the server, it does not predate the service date, it respects the order of the
/// departures already recorded, and the journey is dated today or yesterday in the railway time
/// zone.
/// </summary>
public sealed class DepartureLogRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 8, 30, 0, TimeSpan.Zero);

    /// <summary>Local midnight of the journey's service date in the railway zone (UTC+05:30).</summary>
    private static readonly DateTimeOffset ServiceDateStart =
        new(2026, 10, 28, 0, 0, 0, TimeSpan.FromHours(5.5));

    [Fact]
    public void A_departure_exactly_two_minutes_ahead_of_the_server_is_accepted()
    {
        var result = DepartureLogRules.CheckDepartedAt(
            Now.AddMinutes(2),
            Now,
            ServiceDateStart);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void A_departure_more_than_two_minutes_ahead_of_the_server_is_refused()
    {
        var result = DepartureLogRules.CheckDepartedAt(
            Now.AddMinutes(2).AddSeconds(1),
            Now,
            ServiceDateStart);

        Assert.True(result.IsFailure);
        Assert.Equal(DepartureLogErrors.TooFarAhead, result.Error);
    }

    [Fact]
    public void A_departure_in_the_past_within_the_service_date_is_accepted()
    {
        var result = DepartureLogRules.CheckDepartedAt(
            At(8, 2),
            Now,
            ServiceDateStart);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void A_departure_before_the_service_date_began_is_refused()
    {
        var result = DepartureLogRules.CheckDepartedAt(
            ServiceDateStart.AddSeconds(-1),
            Now,
            ServiceDateStart);

        Assert.True(result.IsFailure);
        Assert.Equal(DepartureLogErrors.BeforeServiceDate, result.Error);
    }

    [Fact]
    public void A_departure_at_the_start_of_the_service_date_is_accepted()
    {
        var result = DepartureLogRules.CheckDepartedAt(
            ServiceDateStart,
            Now,
            ServiceDateStart);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Todays_journey_is_open_for_logging()
    {
        var result = DepartureLogRules.CheckServiceDate(new DateOnly(2026, 10, 28), new DateOnly(2026, 10, 28));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Yesterdays_journey_is_open_for_logging_so_a_queue_synced_after_midnight_is_not_lost()
    {
        var result = DepartureLogRules.CheckServiceDate(new DateOnly(2026, 10, 27), new DateOnly(2026, 10, 28));

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData(2026, 10, 26)]
    [InlineData(2026, 10, 29)]
    public void Any_other_service_date_is_refused(int year, int month, int day)
    {
        var result = DepartureLogRules.CheckServiceDate(new DateOnly(year, month, day), new DateOnly(2026, 10, 28));

        Assert.True(result.IsFailure);
        Assert.Equal(DepartureLogErrors.JourneyDateClosed, result.Error);
    }

    [Fact]
    public void A_departure_earlier_than_the_departure_of_a_previous_stop_is_refused()
    {
        var recorded = new[] { Recorded(2, At(8, 10)) };

        var result = DepartureLogRules.CheckTimeOrder(3, At(8, 5), recorded);

        Assert.True(result.IsFailure);
        Assert.Equal("DepartureLog.OutOfTimeOrder", result.Error.Code);
        Assert.Contains("stop 2", result.Error.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void A_departure_later_than_the_departure_of_a_following_stop_is_refused()
    {
        var recorded = new[] { Recorded(2, At(8, 10)) };

        var result = DepartureLogRules.CheckTimeOrder(1, At(8, 15), recorded);

        Assert.True(result.IsFailure);
        Assert.Equal("DepartureLog.OutOfTimeOrder", result.Error.Code);
        Assert.Contains("stop 2", result.Error.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void A_departure_between_the_previous_and_following_departures_is_accepted()
    {
        var recorded = new[] { Recorded(1, At(8, 5)), Recorded(3, At(8, 25)) };

        var result = DepartureLogRules.CheckTimeOrder(2, At(8, 20), recorded);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Departures_at_the_same_instant_are_in_order()
    {
        var recorded = new[] { Recorded(1, At(8, 5)), Recorded(3, At(8, 5)) };

        var result = DepartureLogRules.CheckTimeOrder(2, At(8, 5), recorded);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void The_order_rule_ignores_a_log_for_the_same_stop()
    {
        var recorded = new[] { Recorded(2, At(8, 10)) };

        var result = DepartureLogRules.CheckTimeOrder(2, At(8, 0), recorded);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void A_timestamp_without_an_offset_is_refused()
    {
        var result = DepartureLogRules.ParseDepartedAt("2026-10-28T08:02:00");

        Assert.True(result.IsFailure);
        Assert.Equal(DepartureLogErrors.MissingOffset, result.Error);
    }

    [Fact]
    public void A_timestamp_with_a_numeric_offset_keeps_its_instant()
    {
        var result = DepartureLogRules.ParseDepartedAt("2026-10-28T08:02:00+05:30");

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateTimeOffset(2026, 10, 28, 2, 32, 0, TimeSpan.Zero), result.Value);
        Assert.Equal(TimeSpan.FromMinutes(330), result.Value.Offset);
    }

    [Fact]
    public void A_timestamp_with_a_utc_designator_is_accepted()
    {
        var result = DepartureLogRules.ParseDepartedAt("2026-10-28T08:02:00Z");

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateTimeOffset(2026, 10, 28, 8, 2, 0, TimeSpan.Zero), result.Value);
    }

    [Fact]
    public void A_timestamp_that_is_not_a_date_and_time_is_refused()
    {
        var result = DepartureLogRules.ParseDepartedAt("2026-13-40T08:02:00+05:30");

        Assert.True(result.IsFailure);
        Assert.Equal(DepartureLogErrors.InvalidDepartedAt, result.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_absent_timestamp_is_refused(string? text)
    {
        var result = DepartureLogRules.ParseDepartedAt(text);

        Assert.True(result.IsFailure);
        Assert.Equal(DepartureLogErrors.MissingDepartedAt, result.Error);
    }

    private static DepartureLog Recorded(int stopSequence, DateTimeOffset departedAt)
    {
        var result = DepartureLog.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            stopSequence,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            departedAt,
            delayCause: null,
            departedAt);

        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static DateTimeOffset At(int hour, int minute) =>
        new(2026, 10, 28, hour, minute, 0, TimeSpan.Zero);
}
