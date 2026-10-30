namespace SmartRail.Catering.Application.Webhooks;

/// <summary>
/// The webhook dispatcher's settings, read from the <c>Catering:Webhook</c> configuration section.
/// The poll interval, request timeout and batch size are configurable so a test can run the
/// dispatcher quickly; the retry delays are configurable for the same reason and default to the
/// five-attempt schedule the spec names (webhook-delivery spec, "Retry with backoff"; design D2).
/// </summary>
/// <param name="PollIntervalSeconds">How often the dispatcher looks for a due delivery.</param>
/// <param name="RequestTimeoutSeconds">How long one POST to the provider may take.</param>
/// <param name="BatchSize">How many deliveries one poll attempts at most.</param>
/// <param name="RetryDelaysSeconds">The wait after the first, second, third and fourth failure.</param>
public sealed record WebhookDispatcherOptions(
    int PollIntervalSeconds,
    int RequestTimeoutSeconds,
    int BatchSize,
    IReadOnlyList<int> RetryDelaysSeconds)
{
    /// <summary>The interval used when <c>Catering:Webhook:PollIntervalSeconds</c> is not configured.</summary>
    public const int DefaultPollIntervalSeconds = 1;

    /// <summary>The request timeout used when <c>Catering:Webhook:RequestTimeoutSeconds</c> is not configured.</summary>
    public const int DefaultRequestTimeoutSeconds = 10;

    /// <summary>The batch size used when <c>Catering:Webhook:BatchSize</c> is not configured.</summary>
    public const int DefaultBatchSize = 10;

    /// <summary>A delivery is attempted five times in total before it is abandoned.</summary>
    public const int MaxAttempts = 5;

    /// <summary>The backoff table the spec names: 5 s, 30 s, 2 min, 10 min, 30 min.</summary>
    public static IReadOnlyList<int> DefaultRetryDelaysSeconds { get; } = [5, 30, 120, 600, 1800];

    /// <summary>True when this failure count has reached the retry limit.</summary>
    public static bool IsAbandoned(int failedAttempts) => failedAttempts >= MaxAttempts;

    /// <summary>The wait after <paramref name="failedAttempts"/> consecutive failures.</summary>
    public TimeSpan RetryDelay(int failedAttempts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(failedAttempts, 1);

        var index = Math.Min(failedAttempts, RetryDelaysSeconds.Count) - 1;

        return TimeSpan.FromSeconds(RetryDelaysSeconds[index]);
    }
}
