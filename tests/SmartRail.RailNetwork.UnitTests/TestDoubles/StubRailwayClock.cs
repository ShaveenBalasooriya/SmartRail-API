using SmartRail.RailNetwork.Domain.Abstractions;

namespace SmartRail.RailNetwork.UnitTests.TestDoubles;

/// <summary>
/// A railway clock frozen at one instant in a fixed-offset zone, so every conversion and timestamp
/// an assertion checks is exact and no test depends on the machine's time zone.
/// </summary>
internal sealed class StubRailwayClock(DateOnly today, DateTimeOffset utcNow) : IRailwayClock
{
    /// <summary>The zone the stub uses: five and a half hours ahead of UTC, like the real railway.</summary>
    internal static readonly TimeZoneInfo RailwayZone =
        TimeZoneInfo.CreateCustomTimeZone("Test/Railway", TimeSpan.FromHours(5.5), "Test/Railway", "Test/Railway");

    public TimeZoneInfo TimeZone => RailwayZone;

    public DateOnly Today { get; } = today;

    public DateOnly Tomorrow => Today.AddDays(1);

    public DateTimeOffset UtcNow { get; } = utcNow;

    public DateTimeOffset ToUtc(DateOnly date, TimeOnly localTimeOfDay) =>
        new DateTimeOffset(date.ToDateTime(localTimeOfDay), RailwayZone.BaseUtcOffset).ToUniversalTime();
}
