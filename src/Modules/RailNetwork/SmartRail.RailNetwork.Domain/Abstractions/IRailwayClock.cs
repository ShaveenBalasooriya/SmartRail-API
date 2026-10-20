namespace SmartRail.RailNetwork.Domain.Abstractions;

/// <summary>
/// The railway's calendar and wall clock. Every conversion between a railway calendar day and a UTC
/// instant goes through this port, so the configured time zone is applied once (design D3). The
/// port lives in Domain because the domain's <c>Journey.Create</c> computes its scheduled departure
/// instant through it; the implementation lives in Infrastructure.
/// </summary>
public interface IRailwayClock
{
    /// <summary>The railway's configured time zone.</summary>
    TimeZoneInfo TimeZone { get; }

    /// <summary>Today's calendar day in the railway's time zone.</summary>
    DateOnly Today { get; }

    /// <summary>Tomorrow's calendar day in the railway's time zone.</summary>
    DateOnly Tomorrow { get; }

    /// <summary>The current instant in UTC.</summary>
    DateTimeOffset UtcNow { get; }

    /// <summary>
    /// Converts a local time of day on a railway calendar day to the UTC instant it denotes.
    /// </summary>
    DateTimeOffset ToUtc(DateOnly date, TimeOnly localTimeOfDay);
}
