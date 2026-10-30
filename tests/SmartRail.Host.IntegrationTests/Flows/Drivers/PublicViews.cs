using System.Text.Json;

namespace SmartRail.Host.IntegrationTests.Flows.Drivers;

/// <summary>One journey as the anonymous passenger channel shows it, read into the values a flow asserts on.</summary>
/// <param name="JourneyId">The journey the view describes.</param>
/// <param name="Status">The journey's lifecycle status, as the payload names it.</param>
/// <param name="DelayMinutes">Minutes late at the next calling stop, or null when the journey carries none.</param>
/// <param name="Provenance">How an uncorrected published change reached the channels, or null when there is none.</param>
/// <param name="CorrectedAt">When a published change on the journey was last corrected, if it was.</param>
/// <param name="LastDepartedStation">The station of the last recorded departure, if any.</param>
/// <param name="NextStation">The station the journey calls at next, if any.</param>
/// <param name="Stops">Every stop in sequence order, calling and non-calling.</param>
internal sealed record PublicJourneyView(
    Guid JourneyId,
    string Status,
    int? DelayMinutes,
    string? Provenance,
    DateTimeOffset? CorrectedAt,
    PublicStationRef? LastDepartedStation,
    PublicStationRef? NextStation,
    IReadOnlyList<PublicStopView> Stops)
{
    /// <summary>The stop with this sequence, which the flow's journey always has.</summary>
    public PublicStopView Stop(int sequence) =>
        Stops.Single(stop => stop.Sequence == sequence);
}

/// <summary>One stop as the public view shows it.</summary>
/// <param name="Sequence">The stop's place in the journey.</param>
/// <param name="Station">The station the stop is at.</param>
/// <param name="IsCalling">False for a stop the journey passes without calling.</param>
/// <param name="ScheduledArrival">The timetabled arrival, when there is one.</param>
/// <param name="ScheduledDeparture">The timetabled departure, when there is one.</param>
/// <param name="ExpectedArrival">The arrival the live state currently expects.</param>
/// <param name="ExpectedDeparture">The departure the live state currently expects.</param>
/// <param name="ActualDeparture">The departure the Guard recorded, when it has been recorded.</param>
/// <param name="PlatformId">The platform the stop uses now.</param>
/// <param name="PlatformNumber">The number the platform is shown with.</param>
/// <param name="PlatformChangedAt">When the current platform was published; null for the timetabled one.</param>
/// <param name="Provenance">How an uncorrected platform change reached the channels, or null.</param>
internal sealed record PublicStopView(
    int Sequence,
    PublicStationRef Station,
    bool IsCalling,
    DateTimeOffset? ScheduledArrival,
    DateTimeOffset? ScheduledDeparture,
    DateTimeOffset? ExpectedArrival,
    DateTimeOffset? ExpectedDeparture,
    DateTimeOffset? ActualDeparture,
    Guid? PlatformId,
    string? PlatformNumber,
    DateTimeOffset? PlatformChangedAt,
    string? Provenance);

/// <summary>A station, as a public payload names it.</summary>
internal sealed record PublicStationRef(Guid Id, string? Name);

/// <summary>One platform's board as the station display shows it.</summary>
internal sealed record PublicDisplayFeed(
    IReadOnlyList<PublicDisplayDeparture> Departures,
    IReadOnlyList<PublicMovedNotice> MovedNotices)
{
    /// <summary>The board's item for this journey, or null when the journey is not on it.</summary>
    public PublicDisplayDeparture? ForJourney(Guid journeyId) =>
        Departures.SingleOrDefault(departure => departure.Journey.JourneyId == journeyId);

    /// <summary>The move notice this journey left on the board, or null when it left none.</summary>
    public PublicMovedNotice? NoticeFor(Guid journeyId) =>
        MovedNotices.SingleOrDefault(notice => notice.JourneyId == journeyId);
}

/// <summary>One upcoming departure of a board, with the whole journey view it was built from.</summary>
/// <param name="Journey">The journey, with every stop and its times.</param>
/// <param name="Origin">The journey's stop at the station the board belongs to.</param>
/// <param name="MinutesLate">Minutes early or late at the origin, or null when the stop carries none.</param>
internal sealed record PublicDisplayDeparture(
    PublicJourneyView Journey,
    PublicStopView Origin,
    int? MinutesLate);

/// <summary>A train that left this platform for another, as the old platform's display announces it.</summary>
/// <param name="JourneyId">The journey that moved.</param>
/// <param name="NewPlatformNumber">The platform the journey is assigned to now.</param>
/// <param name="Message">The full sentence the display shows.</param>
internal sealed record PublicMovedNotice(Guid JourneyId, string? NewPlatformNumber, string? Message);

/// <summary>
/// Reads the public payloads into the values a flow asserts on. The JSON shape is the channel's
/// concern and lives here, so a flow says "the stop's expected arrival moved" without naming a
/// property path.
/// </summary>
internal static class PublicViewJson
{
    /// <summary>Reads one journey view from the detail's <c>data</c> object or a board item's <c>journey</c>.</summary>
    public static PublicJourneyView Journey(JsonElement journey) => new(
        journey.GetProperty("journeyId").GetGuid(),
        journey.GetProperty("status").GetString()!,
        OptionalInt(journey, "delayMinutes"),
        OptionalString(journey, "provenance"),
        OptionalDate(journey, "correctedAt"),
        Station(journey, "lastDepartedStation"),
        Station(journey, "nextStation"),
        [.. journey.GetProperty("stops").EnumerateArray().Select(Stop)]);

    /// <summary>Reads one stop view.</summary>
    public static PublicStopView Stop(JsonElement stop) => new(
        stop.GetProperty("sequence").GetInt32(),
        new PublicStationRef(
            stop.GetProperty("station").GetProperty("id").GetGuid(),
            OptionalString(stop.GetProperty("station"), "name")),
        stop.GetProperty("isCalling").GetBoolean(),
        OptionalDate(stop, "scheduledArrival"),
        OptionalDate(stop, "scheduledDeparture"),
        OptionalDate(stop, "expectedArrival"),
        OptionalDate(stop, "expectedDeparture"),
        OptionalDate(stop, "actualDeparture"),
        OptionalGuid(stop, "platform"),
        OptionalString(stop, "platform", "number"),
        OptionalDate(stop, "platformChangedAt"),
        OptionalString(stop, "provenance"));

    /// <summary>Reads one platform board from the display feed's <c>data</c> object.</summary>
    public static PublicDisplayFeed Feed(JsonElement data) => new(
        [.. data.GetProperty("departures").EnumerateArray().Select(departure => new PublicDisplayDeparture(
            Journey(departure.GetProperty("journey")),
            Stop(departure.GetProperty("origin")),
            OptionalInt(departure, "minutesLate")))],
        [.. data.GetProperty("movedNotices").EnumerateArray().Select(notice => new PublicMovedNotice(
            notice.GetProperty("journeyId").GetGuid(),
            OptionalString(notice, "newPlatform", "number"),
            OptionalString(notice, "message")))]);

    private static PublicStationRef? Station(JsonElement element, string property) =>
        element.TryGetProperty(property, out var station) && station.ValueKind is not JsonValueKind.Null
            ? new PublicStationRef(
                station.GetProperty("id").GetGuid(),
                OptionalString(station, "name"))
            : null;

    private static string? OptionalString(JsonElement element, params string[] path)
    {
        if (!TryNavigate(element, out var value, path) || value.ValueKind is JsonValueKind.Null)
        {
            return null;
        }

        return value.GetString();
    }

    private static Guid? OptionalGuid(JsonElement element, params string[] path) =>
        TryNavigate(element, out var value, path) && value.ValueKind is not JsonValueKind.Null
            ? value.GetProperty("id").GetGuid()
            : null;

    private static DateTimeOffset? OptionalDate(JsonElement element, params string[] path) =>
        TryNavigate(element, out var value, path) && value.ValueKind is not JsonValueKind.Null
            ? value.GetDateTimeOffset()
            : null;

    private static int? OptionalInt(JsonElement element, params string[] path) =>
        TryNavigate(element, out var value, path) && value.ValueKind is not JsonValueKind.Null
            ? value.GetInt32()
            : null;

    /// <summary>Follows a property path, so an absent or null parent reads as "not there".</summary>
    private static bool TryNavigate(JsonElement element, out JsonElement value, params string[] path)
    {
        value = element;

        foreach (var property in path)
        {
            if (value.ValueKind is JsonValueKind.Null
                || !value.TryGetProperty(property, out value))
            {
                return false;
            }
        }

        return true;
    }
}
