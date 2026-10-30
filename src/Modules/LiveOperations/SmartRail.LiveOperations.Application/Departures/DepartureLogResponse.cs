namespace SmartRail.LiveOperations.Application.Departures;

/// <summary>
/// The stored departure log as the Guard app receives it after a 201 or a 200 (departure-logging
/// spec, "Response lets the app show queue status"). <paramref name="DepartedAt"/> is the device time
/// of the tap, normalised to UTC; <paramref name="ReceivedAt"/> is the server time the log arrived,
/// kept separate so a queued sync can be marked done without pretending it happened now.
/// </summary>
/// <param name="Id">The client-generated identifier the device sent.</param>
/// <param name="JourneyId">The journey the departure belongs to.</param>
/// <param name="StopSequence">The calling stop that departed.</param>
/// <param name="DepartedAt">The device time of the tap, which progress uses.</param>
/// <param name="DelayCause">The delay cause when one was given, otherwise <see langword="null"/>.</param>
/// <param name="ReceivedAt">The server time the log was received.</param>
public sealed record DepartureLogResponse(
    Guid Id,
    Guid JourneyId,
    int StopSequence,
    DateTimeOffset DepartedAt,
    string? DelayCause,
    DateTimeOffset ReceivedAt);
