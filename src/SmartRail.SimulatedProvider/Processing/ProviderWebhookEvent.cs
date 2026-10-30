using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartRail.SimulatedProvider.Processing;

/// <summary>
/// One webhook delivery SmartRail pushed, in the versioned contract of design D5. The values bag is
/// left as JSON because each event type carries different values.
/// </summary>
public sealed record ProviderWebhookEvent
{
    /// <summary>The payload contract's version, one in MVP 1.</summary>
    public int Version { get; init; }

    /// <summary>The journey event's identifier, which is what de-duplication uses.</summary>
    public Guid EventId { get; init; }

    /// <summary>The journey's per-journey sequence number.</summary>
    public long SequenceNumber { get; init; }

    /// <summary>The snake-case pushed type, for example <c>delay_published</c>.</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>The journey the change belongs to.</summary>
    public Guid JourneyId { get; init; }

    /// <summary>The stop the change names, when it names one.</summary>
    public int? StopSequence { get; init; }

    /// <summary>The station of <see cref="StopSequence"/>, when it names one.</summary>
    public Guid? StationId { get; init; }

    /// <summary>The new values the type carries.</summary>
    public IReadOnlyDictionary<string, JsonElement>? Values { get; init; }

    /// <summary>When the change was published.</summary>
    public DateTimeOffset PublishedAt { get; init; }

    /// <summary>The provenance label.</summary>
    public string? Provenance { get; init; }

    /// <summary>For a correction, the event it withdraws.</summary>
    public Guid? CorrectsEventId { get; init; }

    /// <summary>The delay in minutes a delay event carries, when it carries one.</summary>
    public int? DelayMinutes => Value("delayMinutes") is { } value && value.ValueKind is JsonValueKind.Number
        ? value.GetInt32()
        : null;

    /// <summary>The new platform's number a platform change carries, when it carries one.</summary>
    public string? PlatformNumber => Value("platformNumber") is { ValueKind: JsonValueKind.String } value
        ? value.GetString()
        : null;

    /// <summary>The JSON serializer the webhook endpoint reads a payload with.</summary>
    public static JsonSerializerOptions JsonOptions { get; } = CreateOptions();

    private JsonElement? Value(string name) =>
        Values is not null && Values.TryGetValue(name, out var value) ? value : null;

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));

        return options;
    }
}
