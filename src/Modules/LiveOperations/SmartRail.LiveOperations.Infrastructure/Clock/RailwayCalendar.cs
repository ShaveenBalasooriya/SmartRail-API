using SmartRail.LiveOperations.Application.Abstractions;

namespace SmartRail.LiveOperations.Infrastructure.Clock;

/// <summary>
/// Converts an instant to its railway calendar day in the railway's configured time zone. The Rail
/// Network's clock applies the same zone to the timetable, so both modules agree on what "today" is.
/// </summary>
internal sealed class RailwayCalendar(TimeZoneInfo timeZone) : IRailwayCalendar
{
    /// <inheritdoc />
    public DateOnly DayOf(DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, timeZone).DateTime);

    /// <inheritdoc />
    public DateTimeOffset StartOfDay(DateOnly day)
    {
        // The local midnight is unspecified so the zone's offset for that date is applied, which is
        // what makes the boundary correct on the days a daylight-saving change falls.
        var midnight = day.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(midnight, timeZone.GetUtcOffset(midnight));
    }
}
