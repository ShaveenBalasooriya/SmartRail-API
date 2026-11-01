namespace SmartRail.LiveOperations.UnitTests.TestDoubles;

/// <summary>A clock frozen at one instant, so every timestamp assertion is exact.</summary>
internal sealed class StubTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
