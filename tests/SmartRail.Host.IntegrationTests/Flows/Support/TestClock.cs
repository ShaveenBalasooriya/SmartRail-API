namespace SmartRail.Host.IntegrationTests.Flows.Support;

/// <summary>
/// The flow's clock: a fixed instant. Every flow starts on a frozen clock, so the times the Host
/// writes are the flow's own and an assertion on a published time or an audit entry's server time is
/// exact rather than "roughly now".
/// </summary>
internal sealed class TestClock(DateTimeOffset now) : TimeProvider
{
    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => now;
}
