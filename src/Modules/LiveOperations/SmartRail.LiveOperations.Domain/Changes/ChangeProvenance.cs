namespace SmartRail.LiveOperations.Domain.Changes;

/// <summary>
/// How a published change reached the channels. MVP 1 has one label: every structured change is
/// published automatically, with no review step (M-16, W-08, W-09).
/// </summary>
public enum ChangeProvenance
{
    /// <summary>The change took effect as soon as the controller published it.</summary>
    AutomaticallyPublished,
}
