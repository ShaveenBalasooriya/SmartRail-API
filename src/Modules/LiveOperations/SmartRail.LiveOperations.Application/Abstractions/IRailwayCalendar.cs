namespace SmartRail.LiveOperations.Application.Abstractions;

/// <summary>
/// The railway's calendar in the railway's time zone. Live Operations cannot reference the Rail
/// Network clock, so it converts through this port; the implementation reads the same configured
/// time zone the Rail Network does.
/// </summary>
public interface IRailwayCalendar
{
    /// <summary>The railway calendar day that <paramref name="instant"/> falls on.</summary>
    DateOnly DayOf(DateTimeOffset instant);

    /// <summary>
    /// The instant the railway calendar day began: local midnight of <paramref name="day"/> in the
    /// railway's time zone. A departure log may not predate it, and the journey's day has to be
    /// today or yesterday by this calendar (departure-logging spec, "Plausible timestamps").
    /// </summary>
    DateTimeOffset StartOfDay(DateOnly day);
}
