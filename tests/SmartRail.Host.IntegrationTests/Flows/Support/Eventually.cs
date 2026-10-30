namespace SmartRail.Host.IntegrationTests.Flows.Support;

/// <summary>
/// Waits for work the server finishes after it answered. A refused write is audited by the event bus
/// shortly after the 403, so a flow that asserts on that entry polls instead of sleeping a fixed time:
/// the wait ends as soon as the expectation holds, and a wait that never holds fails with the thing
/// that was awaited and the last value actually observed.
/// </summary>
internal static class Eventually
{
    /// <summary>How long a delivered side effect may take before the flow fails.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    /// <summary>How often the poll re-reads the value.</summary>
    public static readonly TimeSpan Step = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Polls <paramref name="read"/> until <paramref name="until"/> accepts the value and returns it, or
    /// fails naming what was awaited and the last value seen.
    /// </summary>
    public static async Task<T> ValueAsync<T>(Func<Task<T>> read, Func<T, bool> until, string behaviour)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(until);

        var deadline = DateTimeOffset.UtcNow.Add(Timeout);

        while (true)
        {
            var value = await read();

            if (until(value))
            {
                return value;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                Assert.Fail(
                    $"{behaviour} within {Timeout.TotalSeconds} seconds, but the last value observed was "
                    + $"{Describe(value)}.");
            }

            await Task.Delay(Step);
        }
    }

    /// <summary>
    /// Watches <paramref name="appeared"/> for a short window and fails if it ever holds. For the one
    /// check a poll cannot express: that something the server should not do does not happen at all.
    /// The window is longer than one dispatcher poll, so a delivery that was going to happen would.
    /// </summary>
    public static async Task NothingAsync(Func<Task<bool>> appeared, string behaviour, TimeSpan? window = null)
    {
        ArgumentNullException.ThrowIfNull(appeared);

        var quietWindow = window ?? TimeSpan.FromSeconds(2);
        var until = DateTimeOffset.UtcNow.Add(quietWindow);

        while (DateTimeOffset.UtcNow < until)
        {
            if (await appeared())
            {
                Assert.Fail(
                    $"{behaviour}, but it did inside the {quietWindow.TotalSeconds}-second window the flow watched.");
            }

            await Task.Delay(Step);
        }
    }

    private static string Describe(object? value) => value switch
    {
        null => "(null)",
        string text => $"'{text}'",
        System.Collections.IEnumerable items => "["
            + string.Join(", ", items.Cast<object?>().Select(Describe))
            + "]",
        _ => Verify.Describe(value),
    };
}
