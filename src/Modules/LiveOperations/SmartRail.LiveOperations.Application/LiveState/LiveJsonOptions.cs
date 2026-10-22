using System.Text.Json;
using System.Text.Json.Serialization;
using SmartRail.LiveOperations.Contracts;

namespace SmartRail.LiveOperations.Application.LiveState;

/// <summary>
/// The single set of JSON options for a journey event's <c>payload</c> snapshot (design D6). The
/// serializer writes a <see cref="LiveJourneyView"/> with these options and the event queries read
/// it back with the same ones, so the payload needs no version marker.
/// </summary>
internal static class LiveJsonOptions
{
    /// <summary>
    /// Web defaults — camel-case property names, case-insensitive reads — plus enums as camel-case
    /// strings, so a stored snapshot names a status instead of depending on an enum's numeric value.
    /// </summary>
    public static readonly JsonSerializerOptions Default = CreateDefault();

    private static JsonSerializerOptions CreateDefault()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
