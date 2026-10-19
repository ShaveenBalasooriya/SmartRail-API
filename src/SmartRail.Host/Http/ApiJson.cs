using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartRail.Host.Http;

/// <summary>
/// The JSON rules from the <c>api-conventions</c> spec: camelCase property names, enums as
/// lower-case snake_case strings, and timestamps as ISO 8601 UTC instants ending in <c>Z</c>.
/// </summary>
public static class ApiJson
{
    /// <summary>The single serializer configuration every endpoint and client shares.</summary>
    public static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        Configure(options);
        return options;
    }

    /// <summary>Applies the SmartRail JSON rules to an existing options instance.</summary>
    public static void Configure(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        options.Converters.Add(new UtcDateTimeOffsetConverter());
    }
}

/// <summary>
/// Writes every timestamp as an ISO 8601 UTC instant ending in <c>Z</c>, and accepts only
/// timestamps that carry an offset. A timestamp without an offset is ambiguous, and M-02 freshness
/// depends on unambiguous times, so it is rejected as invalid input.
/// </summary>
public sealed class UtcDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    /// <summary>The exact wire format: second precision, no offset, always <c>Z</c>.</summary>
    public const string Format = "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'";

    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("A timestamp must be a JSON string.");
        }

        var text = reader.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new JsonException("A timestamp cannot be empty.");
        }

        // DateTimeOffset.Parse applies the machine's local offset when the text has none, which is
        // exactly the ambiguity this rule exists to reject.
        if (!HasOffset(text))
        {
            throw new JsonException($"A timestamp must include a UTC offset, for example 2026-10-19T02:45:00Z. Received '{text}'.");
        }

        if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value))
        {
            throw new JsonException($"'{text}' is not a valid timestamp.");
        }

        return value.ToUniversalTime();
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.ToUniversalTime().ToString(Format, CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// True when the text ends with <c>Z</c> or carries a numeric offset such as <c>+05:30</c>.
    /// </summary>
    public static bool HasOffset(string text)
    {
        if (text.EndsWith('Z') || text.EndsWith('z'))
        {
            return true;
        }

        // A time component always precedes an offset, and a date alone ("2026-10-19") has no 'T'.
        var timeStart = text.IndexOf('T', StringComparison.Ordinal);
        if (timeStart < 0)
        {
            return false;
        }

        var offsetStart = text.IndexOfAny(['+', '-'], timeStart);
        return offsetStart > timeStart;
    }
}
