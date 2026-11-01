namespace SmartRail.IdentityAccess.UnitTests.TestDoubles;

/// <summary>A clock frozen at one instant.</summary>
internal sealed class StubTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
