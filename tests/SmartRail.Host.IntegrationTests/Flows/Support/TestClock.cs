namespace SmartRail.Host.IntegrationTests.Flows.Support;

/// <summary>
/// The flow's clock: a fixed instant a flow can also move forward. Every flow starts on a frozen
/// clock, so the times the Host writes are the flow's own; a flow that has to wait for a scheduled
/// retry moves the clock instead of sleeping, because the dispatcher compares its schedule with the
/// same clock the Host reads.
/// </summary>
internal sealed class TestClock(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>Moves the clock forward, as the elapsed time a retry schedule waits for.</summary>
    public void Advance(TimeSpan by) => _now = _now.Add(by);
}
