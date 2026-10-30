namespace SmartRail.LiveOperations.Application.Abstractions;

/// <summary>
/// The Live Operations settings read from the <c>Operations</c> configuration section.
/// </summary>
/// <param name="StaleAfterMinutes">
/// How long a running journey may go without an update before the live view flags it as stale.
/// </param>
/// <param name="CompletionCheckSeconds">
/// How often the background check completes journeys whose terminus arrival has passed.
/// </param>
public sealed record OperationsOptions(int StaleAfterMinutes, int CompletionCheckSeconds)
{
    /// <summary>The threshold used when <c>Operations:StaleAfterMinutes</c> is not configured.</summary>
    public const int DefaultStaleAfterMinutes = 10;

    /// <summary>The interval used when <c>Operations:CompletionCheckSeconds</c> is not configured.</summary>
    public const int DefaultCompletionCheckSeconds = 60;
}
