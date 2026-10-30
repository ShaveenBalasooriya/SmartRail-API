using System.Globalization;
using SmartRail.SharedKernel.Results;

namespace SmartRail.LiveOperations.Domain.Departures;

/// <summary>
/// The plausibility rules a new departure log is checked against before it is stored or applied
/// (departure-logging spec, "Plausible timestamps" and "Journeys open for logging"). Everything here
/// is pure: the calling use case supplies the server time, the railway calendar's start of the
/// journey's service date and the departures already on record, so the rules are unit-testable
/// without a database or a clock.
/// </summary>
/// <remarks>
/// The logging use case (DL3) uses these in this order: <see cref="ParseDepartedAt"/> to read the
/// device timestamp and refuse one without an offset, <see cref="CheckServiceDate"/> with the
/// railway calendar's day of the server time, <c>JourneyLiveState.CheckDepartureCanBeLogged</c> for
/// the stop, <see cref="CheckDepartedAt"/> with the calendar's
/// <c>StartOfDay(journey.ServiceDate)</c>, and <see cref="CheckTimeOrder"/> with the logs read for
/// the journey.
/// </remarks>
public static class DepartureLogRules
{
    /// <summary>
    /// How far a device clock may run ahead of the server before the log is refused. A small skew is
    /// accepted because the tap time is the Guard's clock, which may be a little fast (risk noted in
    /// design "Risks / Trade-offs").
    /// </summary>
    public static readonly TimeSpan MaximumAheadOfServer = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Parses the device timestamp, refusing text without an explicit UTC offset: an offset-less
    /// value is ambiguous and would be interpreted in the server's local zone. The timestamp is
    /// bound as text rather than a <see cref="DateTimeOffset"/> so this rule is applied before the
    /// model binder has silently assumed an offset.
    /// </summary>
    public static Result<DateTimeOffset> ParseDepartedAt(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Result.Failure<DateTimeOffset>(DepartureLogErrors.MissingDepartedAt);
        }

        if (!HasExplicitOffset(text))
        {
            return Result.Failure<DateTimeOffset>(DepartureLogErrors.MissingOffset);
        }

        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)
            ? Result.Success(value)
            : Result.Failure<DateTimeOffset>(DepartureLogErrors.InvalidDepartedAt);
    }

    /// <summary>
    /// Refuses a timestamp more than two minutes ahead of the server and one that falls before the
    /// journey's service date began in the railway time zone. The boundary is inclusive: exactly two
    /// minutes ahead is accepted, and a timestamp equal to the start of the service date is accepted.
    /// </summary>
    public static Result CheckDepartedAt(
        DateTimeOffset departedAt,
        DateTimeOffset now,
        DateTimeOffset serviceDateStart)
    {
        if (departedAt > now.Add(MaximumAheadOfServer))
        {
            return Result.Failure(DepartureLogErrors.TooFarAhead);
        }

        return departedAt < serviceDateStart
            ? Result.Failure(DepartureLogErrors.BeforeServiceDate)
            : Result.Success();
    }

    /// <summary>
    /// Only a journey dated today or yesterday in the railway time zone is open for logging, so a
    /// queue synced just after midnight is not lost while anything older is refused
    /// (departure-logging spec, "Journeys open for logging").
    /// </summary>
    public static Result CheckServiceDate(DateOnly serviceDate, DateOnly today)
    {
        return serviceDate == today || serviceDate == today.AddDays(-1)
            ? Result.Success()
            : Result.Failure(DepartureLogErrors.JourneyDateClosed);
    }

    /// <summary>
    /// Refuses a departure that contradicts a departure already recorded for the journey: it may not
    /// be earlier than a previous stop's departure or later than a following stop's departure.
    /// Departures at the same instant are allowed, because two stops may genuinely depart together.
    /// The log for the stop being recorded is ignored; the unique index settles a second log for it.
    /// </summary>
    public static Result CheckTimeOrder(
        int stopSequence,
        DateTimeOffset departedAt,
        IEnumerable<DepartureLog> recorded)
    {
        ArgumentNullException.ThrowIfNull(recorded);

        foreach (var log in recorded)
        {
            if (log.StopSequence == stopSequence)
            {
                continue;
            }

            if (log.StopSequence < stopSequence && departedAt < log.DepartedAt)
            {
                return Result.Failure(DepartureLogErrors.OutOfTimeOrder(log.StopSequence));
            }

            if (log.StopSequence > stopSequence && departedAt > log.DepartedAt)
            {
                return Result.Failure(DepartureLogErrors.OutOfTimeOrder(log.StopSequence));
            }
        }

        return Result.Success();
    }

    /// <summary>
    /// Whether the text carries its own offset, either <c>Z</c> or a <c>±hh:mm</c> after the time
    /// part. A date's own dashes appear before the time and are not mistaken for an offset.
    /// </summary>
    private static bool HasExplicitOffset(string text)
    {
        var trimmed = text.Trim();

        if (trimmed.EndsWith('Z') || trimmed.EndsWith('z'))
        {
            return true;
        }

        var timeStart = trimmed.IndexOf('T');
        if (timeStart < 0)
        {
            timeStart = trimmed.IndexOf(' ');
        }

        if (timeStart < 0)
        {
            return false;
        }

        return trimmed.IndexOfAny(['+', '-'], timeStart) > timeStart;
    }
}
