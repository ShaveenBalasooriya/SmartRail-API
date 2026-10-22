using SmartRail.RailNetwork.Domain.Abstractions;

namespace SmartRail.RailNetwork.Infrastructure.Clock;

/// <summary>
/// The railway's calendar and wall clock (design D3). Every conversion between a railway calendar
/// day and a UTC instant goes through this type, so the configured time zone is applied once.
/// </summary>
internal sealed class RailwayClock : IRailwayClock
{
    /// <summary>The time zone used when <c>Railway:TimeZone</c> is not configured.</summary>
    public const string DefaultTimeZoneId = "Asia/Colombo";

    private readonly TimeProvider _timeProvider;
    private readonly TimeZoneInfo _timeZone;

    /// <summary>Creates the clock from the current-time source and the railway's time zone.</summary>
    public RailwayClock(TimeProvider timeProvider, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(timeZone);

        _timeProvider = timeProvider;
        _timeZone = timeZone;
    }

    /// <inheritdoc />
    public TimeZoneInfo TimeZone => _timeZone;

    /// <inheritdoc />
    public DateOnly Today => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), _timeZone).DateTime);

    /// <inheritdoc />
    public DateOnly Tomorrow => Today.AddDays(1);

    /// <inheritdoc />
    public DateTimeOffset UtcNow => _timeProvider.GetUtcNow();

    /// <inheritdoc />
    public DateTimeOffset ToUtc(DateOnly date, TimeOnly localTimeOfDay)
    {
        var local = date.ToDateTime(localTimeOfDay, DateTimeKind.Unspecified);
        var utc = TimeZoneInfo.ConvertTimeToUtc(local, _timeZone);

        return new DateTimeOffset(utc, TimeSpan.Zero);
    }
}
