using System.Globalization;
using System.Text.Json;

namespace SmartRail.Host.IntegrationTests.Flows.Drivers;

/// <summary>
/// One journey as a staff view returns it, read into the values a flow compares. The controller's
/// detail and the Guard's detail carry the same journey, stop times, platform and published changes;
/// they differ only in the fields each role is spared, so one shape renders both and the two
/// renderings can be compared directly.
/// </summary>
/// <param name="JourneyId">The journey the view describes.</param>
/// <param name="Status">The journey's lifecycle status, as the payload names it.</param>
/// <param name="DelayMinutes">Minutes late at the next calling stop, or null when the journey carries none.</param>
/// <param name="Stops">The journey's stops in sequence order.</param>
/// <param name="Changes">The changes published on the journey, in publish order.</param>
internal sealed record StaffJourneyView(
    Guid JourneyId,
    string Status,
    int? DelayMinutes,
    IReadOnlyList<StaffStopView> Stops,
    IReadOnlyList<StaffChangeView> Changes)
{
    /// <summary>The stop with this sequence.</summary>
    public StaffStopView Stop(int sequence) => Stops.Single(stop => stop.Sequence == sequence);

    /// <summary>The change with this identifier.</summary>
    public StaffChangeView Change(Guid changeId) => Changes.Single(change => change.Id == changeId);

    /// <summary>
    /// The journey and its changes as one comparable text, so a controller view and a Guard view that
    /// must agree are compared by what they say rather than by the order a serialiser wrote it in.
    /// </summary>
    public string Render() =>
        $"journey {JourneyId} {Status} delay={DelayMinutes?.ToString(CultureInfo.InvariantCulture) ?? "-"} "
        + "stops["
        + string.Join(
            " ; ",
            Stops.Select(stop =>
                $"{stop.Sequence}@{stop.StationId} arr={Moment(stop.ScheduledArrival)}/{Moment(stop.ExpectedArrival)} "
                + $"dep={Moment(stop.ScheduledDeparture)}/{Moment(stop.ExpectedDeparture)} "
                + $"act={Moment(stop.ActualDeparture)} pl={stop.PlatformNumber ?? "-"}"
                + $"@{Moment(stop.PlatformChangedAt)}"))
        + "] changes["
        + string.Join(
            " ; ",
            Changes.Select(change =>
                $"{change.Id} {change.Type} published={Moment(change.PublishedAt)} corrected={change.Corrected} "
                + string.Join(
                    ",",
                    change.Values.OrderBy(value => value.Key, StringComparer.Ordinal)
                        .Select(value => $"{value.Key}={value.Value}"))))
        + "]";

    private static string Moment(DateTimeOffset? moment) =>
        moment?.ToString("O", CultureInfo.InvariantCulture) ?? "-";
}

/// <summary>One stop as a staff view returns it.</summary>
/// <param name="Sequence">The stop's place in the journey.</param>
/// <param name="StationId">The station the stop is at.</param>
/// <param name="ScheduledArrival">The timetabled arrival, when there is one.</param>
/// <param name="ScheduledDeparture">The timetabled departure, when there is one.</param>
/// <param name="ExpectedArrival">The arrival the live state currently expects.</param>
/// <param name="ExpectedDeparture">The departure the live state currently expects.</param>
/// <param name="ActualDeparture">The departure the Guard recorded, when it has been recorded.</param>
/// <param name="PlatformId">The platform the stop uses now.</param>
/// <param name="PlatformNumber">The number the platform is shown with.</param>
/// <param name="PlatformChangedAt">When the current platform was published; null for the timetabled one.</param>
internal sealed record StaffStopView(
    int Sequence,
    Guid? StationId,
    DateTimeOffset? ScheduledArrival,
    DateTimeOffset? ScheduledDeparture,
    DateTimeOffset? ExpectedArrival,
    DateTimeOffset? ExpectedDeparture,
    DateTimeOffset? ActualDeparture,
    Guid? PlatformId,
    string? PlatformNumber,
    DateTimeOffset? PlatformChangedAt);

/// <summary>One published change as a staff view returns it.</summary>
/// <param name="Id">The change's identifier.</param>
/// <param name="Type">The kind of change, for example <c>correction</c>.</param>
/// <param name="Values">The values the change set, keyed by field name.</param>
/// <param name="PublishedAt">When the change was published.</param>
/// <param name="Corrected">True once a correction has withdrawn it.</param>
internal sealed record StaffChangeView(
    Guid Id,
    string Type,
    IReadOnlyDictionary<string, string?> Values,
    DateTimeOffset PublishedAt,
    bool Corrected);

/// <summary>Reads the staff views' payloads, so the JSON shape stays out of the flows.</summary>
internal static class StaffViewJson
{
    /// <summary>
    /// Reads one staff detail: the controller's carries the stops beside the journey, the Guard's
    /// carries them inside it, and both carry the changes beside it.
    /// </summary>
    /// <param name="detail">The detail response's root object.</param>
    /// <param name="stopsInsideJourney">True for the Guard's shape, whose journey holds its stops.</param>
    public static StaffJourneyView Detail(JsonElement detail, bool stopsInsideJourney)
    {
        var journey = detail.GetProperty("journey");
        var stops = stopsInsideJourney ? journey.GetProperty("stops") : detail.GetProperty("stops");
        var changes = detail.GetProperty("changes");

        return new StaffJourneyView(
            journey.GetProperty("id").GetGuid(),
            journey.GetProperty("status").GetString()!,
            OptionalInt(journey, "delayMinutes"),
            [.. stops.EnumerateArray().Select(Stop)],
            [.. changes.EnumerateArray().Select(Change)]);
    }

    private static StaffStopView Stop(JsonElement stop) => new(
        stop.GetProperty("sequence").GetInt32(),
        stop.TryGetProperty("station", out var station) && station.ValueKind is not System.Text.Json.JsonValueKind.Null
            ? station.GetProperty("id").GetGuid()
            : null,
        OptionalDate(stop, "scheduledArrival"),
        OptionalDate(stop, "scheduledDeparture"),
        OptionalDate(stop, "expectedArrival"),
        OptionalDate(stop, "expectedDeparture"),
        OptionalDate(stop, "actualDeparture"),
        OptionalGuid(stop, "platform"),
        OptionalString(stop, "platform", "number"),
        OptionalDate(stop, "platformChangedAt"));

    private static StaffChangeView Change(JsonElement change) => new(
        change.GetProperty("id").GetGuid(),
        change.GetProperty("type").GetString()!,
        change.GetProperty("values").EnumerateObject()
            .ToDictionary(
                property => property.Name,
                property => property.Value.ValueKind is System.Text.Json.JsonValueKind.Null
                    ? null
                    : property.Value.GetString()),
        change.GetProperty("publishedAt").GetDateTimeOffset(),
        change.GetProperty("corrected").GetBoolean());

    private static string? OptionalString(JsonElement element, params string[] path) =>
        TryNavigate(element, out var value, path) && value.ValueKind is not System.Text.Json.JsonValueKind.Null
            ? value.GetString()
            : null;

    private static Guid? OptionalGuid(JsonElement element, params string[] path) =>
        TryNavigate(element, out var value, path) && value.ValueKind is not System.Text.Json.JsonValueKind.Null
            ? value.GetProperty("id").GetGuid()
            : null;

    private static DateTimeOffset? OptionalDate(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind is not System.Text.Json.JsonValueKind.Null
            ? value.GetDateTimeOffset()
            : null;

    private static int? OptionalInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind is not System.Text.Json.JsonValueKind.Null
            ? value.GetInt32()
            : null;

    private static bool TryNavigate(JsonElement element, out JsonElement value, params string[] path)
    {
        value = element;

        foreach (var property in path)
        {
            if (value.ValueKind is System.Text.Json.JsonValueKind.Null
                || !value.TryGetProperty(property, out value))
            {
                return false;
            }
        }

        return true;
    }
}
