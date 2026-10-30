using System.Globalization;
using System.Text;
using System.Text.Json;

namespace SmartRail.Audit.Domain.Entries;

/// <summary>
/// Produces the canonical JSON that an entry's hash covers (design D3): object keys sorted with
/// ordinal comparison, no insignificant whitespace, nested before/after JSON canonicalised the same
/// way, and <c>occurred_at</c> as a UTC timestamp with microsecond precision.
/// </summary>
public static class AuditEntryCanonicalizer
{
    /// <summary>
    /// The canonical JSON of the fields the hash covers. <see cref="AuditEntry.SourceEventId"/> is
    /// deliberately absent, because a denial's event ID is metadata about delivery, not content
    /// (design D5).
    /// </summary>
    public static string Canonicalize(AuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();

            // Written in ordinal key order, which is what makes the form canonical.
            writer.WriteString("action", entry.Action);
            writer.WriteString("actor_role", entry.ActorRole);
            WriteGuid(writer, "actor_staff_id", entry.ActorStaffId);
            WriteJson(writer, "after", entry.After);
            WriteJson(writer, "before", entry.Before);
            writer.WriteNumber("chain_position", entry.ChainPosition);
            writer.WriteString("occurred_at", FormatTimestamp(entry.OccurredAt));
            writer.WriteString("outcome", OutcomeToken(entry.Outcome));
            WriteGuid(writer, "related_entry_id", entry.RelatedEntryId);
            writer.WriteString("target_id", entry.TargetId);
            writer.WriteString("target_type", entry.TargetType);

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteGuid(Utf8JsonWriter writer, string name, Guid? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
            return;
        }

        writer.WriteString(name, value.Value.ToString("D", CultureInfo.InvariantCulture));
    }

    private static void WriteJson(Utf8JsonWriter writer, string name, string? json)
    {
        writer.WritePropertyName(name);

        if (json is null)
        {
            writer.WriteNullValue();
            return;
        }

        using var document = JsonDocument.Parse(json);
        WriteElement(writer, document.RootElement);
    }

    private static void WriteElement(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();

                foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteElement(writer, property.Value);
                }

                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();

                foreach (var item in element.EnumerateArray())
                {
                    WriteElement(writer, item);
                }

                writer.WriteEndArray();
                break;

            case JsonValueKind.Undefined:
                writer.WriteNullValue();
                break;

            default:
                // Numbers keep their original text and strings are re-escaped consistently.
                element.WriteTo(writer);
                break;
        }
    }

    private static string FormatTimestamp(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        var microseconds = utc.Ticks - (utc.Ticks % TimeSpan.TicksPerMicrosecond);

        return new DateTimeOffset(microseconds, TimeSpan.Zero)
            .ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture);
    }

    private static string OutcomeToken(AuditOutcome outcome) => outcome switch
    {
        AuditOutcome.Succeeded => "succeeded",
        AuditOutcome.Denied => "denied",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "The audit outcome is not known."),
    };
}
