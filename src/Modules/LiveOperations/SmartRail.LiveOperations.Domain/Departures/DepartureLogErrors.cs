using SmartRail.SharedKernel.Results;

namespace SmartRail.LiveOperations.Domain.Departures;

/// <summary>
/// The expected failures of creating or validating a <see cref="DepartureLog"/>. The logging use
/// case maps each to a problem detail: a shape or plausibility failure is a validation failure, so a
/// log the Guard app must never retry is answered with HTTP 400 (departure-logging design D2).
/// </summary>
public static class DepartureLogErrors
{
    /// <summary>A log cannot exist without the identifier the device generated for it.</summary>
    public static readonly Error MissingId = Error.Validation(
        "DepartureLog.MissingId",
        "A departure log needs the identifier the device generated for it.");

    /// <summary>A log cannot exist without the journey it belongs to.</summary>
    public static readonly Error MissingJourneyId = Error.Validation(
        "DepartureLog.MissingJourneyId",
        "A journey identifier is required.");

    /// <summary>A log cannot exist without the station of the stop it records.</summary>
    public static readonly Error MissingStationId = Error.Validation(
        "DepartureLog.MissingStationId",
        "A departure log needs the station of the stop it records.");

    /// <summary>A log cannot exist without the Guard who recorded it.</summary>
    public static readonly Error MissingGuard = Error.Validation(
        "DepartureLog.MissingGuard",
        "The Guard who logged the departure is required.");

    /// <summary>Stop sequences start at one.</summary>
    public static readonly Error InvalidStopSequence = Error.Validation(
        "DepartureLog.InvalidStopSequence",
        "A departure log needs the sequence of a stop, starting at 1.");

    /// <summary>The delay cause has to be one of the fixed list when it is given at all.</summary>
    public static readonly Error InvalidDelayCause = Error.Validation(
        "DepartureLog.InvalidDelayCause",
        "The delay cause must be one of the fixed list.");

    /// <summary>The device time of the tap is what progress is computed from, so it is required.</summary>
    public static readonly Error MissingDepartedAt = Error.Validation(
        "DepartureLog.MissingDepartedAt",
        "A departure log needs the device time of the tap.");

    /// <summary>The server time of receipt is recorded separately and is required.</summary>
    public static readonly Error MissingReceivedAt = Error.Validation(
        "DepartureLog.MissingReceivedAt",
        "A departure log needs the server time it was received.");

    /// <summary>A device timestamp has to carry its own UTC offset; a bare local time is ambiguous.</summary>
    public static readonly Error MissingOffset = Error.Validation(
        "DepartureLog.MissingOffset",
        "The departure timestamp must include its UTC offset.");

    /// <summary>The timestamp text is not a valid date and time.</summary>
    public static readonly Error InvalidDepartedAt = Error.Validation(
        "DepartureLog.InvalidDepartedAt",
        "The departure timestamp is not a valid date and time.");

    /// <summary>A device clock may run slightly fast, but not by more than the allowed skew.</summary>
    public static readonly Error TooFarAhead = Error.Validation(
        "DepartureLog.TooFarAhead",
        "A departure cannot be more than two minutes ahead of the server time.");

    /// <summary>A departure cannot predate the journey it belongs to.</summary>
    public static readonly Error BeforeServiceDate = Error.Validation(
        "DepartureLog.BeforeServiceDate",
        "A departure cannot be earlier than the start of the journey's service date.");

    /// <summary>The last calling stop is reached, not departed, so it has no departure to log.</summary>
    public static readonly Error StopIsFinalStop = Error.Validation(
        "DepartureLog.StopIsFinalStop",
        "The journey's final calling stop has no departure to log.");

    /// <summary>Only a journey dated today or yesterday in the railway time zone is open for logging.</summary>
    public static readonly Error JourneyDateClosed = Error.Validation(
        "DepartureLog.JourneyDateClosed",
        "Departures can be logged only for a journey dated today or yesterday in the railway time zone.");

    /// <summary>The delay cause text is not one of the seven values the contract names.</summary>
    public static Error UnknownDelayCause(string? value) => Error.Validation(
        "DepartureLog.UnknownDelayCause",
        $"'{value}' is not a delay cause. Use signalling, rolling_stock, crew, passenger_boarding, "
        + "infrastructure, weather or other.");

    /// <summary>The journey has no stop at that sequence.</summary>
    public static Error StopNotFound(int stopSequence) => Error.Validation(
        "DepartureLog.StopNotFound",
        $"The journey has no stop {stopSequence}.");

    /// <summary>The stop exists but the service passes it without stopping, so it cannot depart.</summary>
    public static Error StopNotCalling(int stopSequence) => Error.Validation(
        "DepartureLog.StopNotCalling",
        $"Stop {stopSequence} is not a calling stop of the journey.");

    /// <summary>The departure is out of order against a departure already recorded for the journey.</summary>
    public static Error OutOfTimeOrder(int conflictingStopSequence) => Error.Validation(
        "DepartureLog.OutOfTimeOrder",
        $"The departure is out of time order against stop {conflictingStopSequence}.");

    /// <summary>
    /// The identifier was accepted before with different content, so the resend is not the same log
    /// and cannot be replayed (departure-logging design D3).
    /// </summary>
    public static readonly Error IdAlreadyAccepted = Error.Conflict(
        "DepartureLog.IdAlreadyAccepted",
        "A departure log with this identifier was already accepted with different content.");

    /// <summary>
    /// The stop already has a departure under another client identifier: the tap happened twice, or
    /// a queue item was duplicated with a new identifier (departure-logging spec, "One departure per
    /// stop").
    /// </summary>
    public static readonly Error StopAlreadyLogged = Error.Conflict(
        "DepartureLog.StopAlreadyLogged",
        "A departure has already been recorded for this stop of the journey.");

    /// <summary>The journey is assigned to another Guard, so this Guard may not log it.</summary>
    public static readonly Error NotAssigned = Error.Forbidden(
        "DepartureLog.NotAssigned",
        "The journey is not assigned to the calling guard.");
}
