namespace SmartRail.RailNetwork.Application.Clock;

/// <summary>
/// The railway's calendar and wall clock (design D1). Every conversion between a railway calendar
/// day and a UTC instant goes through this type, so the configured time zone is applied once.
/// </summary>
public sealed class RailwayClock
{
    /// <summary>The time zone used when <c>Railway:TimeZone</c> is not configured.</summary>
    public const string DefaultTimeZoneId = "Asia/Colombo";

    private readonly TimeProvider _timeProvider;
    private readonly TimeZoneInfo _timeZone;

    /// <summary>
    /// Creates the clock from the current-time source and the railway's time zone. The zone is
    /// passed in rather than looked up here so this type has no configuration dependency.
    /// </summary>
    public RailwayClock(TimeProvider timeProvider, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(timeZone);

        _timeProvider = timeProvider;
        _timeZone = timeZone;
    }

    /// <summary>The railway's configured time zone.</summary>
    public TimeZoneInfo TimeZone => _timeZone;

    /// <summary>Today's calendar day in the railway's time zone.</summary>
    public DateOnly Today => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), _timeZone).DateTime);

    /// <summary>Tomorrow's calendar day in the railway's time zone.</summary>
    public DateOnly Tomorrow => Today.AddDays(1);

    /// <summary>
    /// Converts a local time of day on a railway calendar day to the UTC instant it denotes.
    /// </summary>
    public DateTimeOffset ToUtc(DateOnly date, TimeOnly localTimeOfDay)
    {
        var local = date.ToDateTime(localTimeOfDay, DateTimeKind.Unspecified);
        var utc = TimeZoneInfo.ConvertTimeToUtc(local, _timeZone);

        return new DateTimeOffset(utc, TimeSpan.Zero);
    }
}
