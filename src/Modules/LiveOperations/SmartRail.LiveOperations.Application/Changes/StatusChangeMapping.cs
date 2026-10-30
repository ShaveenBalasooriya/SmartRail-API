using System.Globalization;
using SmartRail.LiveOperations.Application.ControllerView;
using SmartRail.LiveOperations.Domain.Changes;

namespace SmartRail.LiveOperations.Application.Changes;

/// <summary>
/// Turns a stored <see cref="StatusChange"/> into the text and values the controller's view, the
/// published response and the calculator expect, so the writer and the readers cannot disagree
/// about what a change means.
/// </summary>
internal static class StatusChangeMapping
{
    /// <summary>The change type as the channels name it, for example <c>platform_change</c>.</summary>
    public static string TypeText(StatusChangeType type) => type switch
    {
        StatusChangeType.Delay => "delay",
        StatusChangeType.Cancellation => "cancellation",
        StatusChangeType.Termination => "termination",
        StatusChangeType.PlatformChange => "platform_change",
        StatusChangeType.Correction => "correction",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown status change type."),
    };

    /// <summary>The provenance as the channels name it, for example <c>automatically_published</c>.</summary>
    public static string ProvenanceText(ChangeProvenance provenance) => provenance switch
    {
        ChangeProvenance.AutomaticallyPublished => "automatically_published",
        _ => throw new ArgumentOutOfRangeException(nameof(provenance), provenance, "Unknown change provenance."),
    };

    /// <summary>
    /// The values the change set, keyed by field name: a delay from its starting stop, a platform
    /// change from its stop and both platforms, a termination from the last stop served, and a
    /// correction from the change it withdraws. A cancellation carries no values.
    /// </summary>
    public static IReadOnlyDictionary<string, string?> ValuesOf(
        StatusChangeType type,
        int? stopSequence,
        int? delayMinutes,
        Guid? newPlatformId,
        Guid? previousPlatformId,
        Guid? correctsChangeId)
    {
        Dictionary<string, string?> values = type switch
        {
            StatusChangeType.Delay => new()
            {
                ["fromStopSequence"] = Text(stopSequence),
                ["minutes"] = Text(delayMinutes),
            },
            StatusChangeType.PlatformChange => new()
            {
                ["stopSequence"] = Text(stopSequence),
                ["previousPlatformId"] = Text(previousPlatformId),
                ["platformId"] = Text(newPlatformId),
            },
            StatusChangeType.Termination => new()
            {
                ["lastStopSequence"] = Text(stopSequence),
            },
            StatusChangeType.Correction => new()
            {
                ["correctsChangeId"] = Text(correctsChangeId),
            },
            StatusChangeType.Cancellation => new Dictionary<string, string?>(),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown status change type."),
        };

        return values;
    }

    /// <summary>The view the controller's history shows for one stored change.</summary>
    public static StatusChangeView ToView(StatusChange change, bool corrected)
    {
        ArgumentNullException.ThrowIfNull(change);

        return new StatusChangeView(
            change.Id,
            TypeText(change.Type),
            ValuesOf(
                change.Type,
                change.StopSequence,
                change.DelayMinutes,
                change.NewPlatformId,
                change.PreviousPlatformId,
                change.CorrectsChangeId),
            change.PublishedBy,
            change.PublishedAt,
            ProvenanceText(change.Provenance),
            corrected);
    }

    private static string? Text(int? value) =>
        value?.ToString(CultureInfo.InvariantCulture);

    private static string? Text(Guid? value) =>
        value?.ToString();
}
