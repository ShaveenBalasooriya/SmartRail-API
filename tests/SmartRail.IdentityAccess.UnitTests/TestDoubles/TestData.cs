using System.Reflection;
using SmartRail.IdentityAccess.Domain.Entities;

namespace SmartRail.IdentityAccess.UnitTests.TestDoubles;

/// <summary>Fresh domain objects for each test.</summary>
internal static class TestData
{
    internal static readonly DateTimeOffset Now = new(2026, 10, 30, 6, 0, 0, TimeSpan.Zero);

    internal const string PlaintextKey = "srk_abcd1234_super-secret";

    internal static ProviderApiKey Key(Guid? providerId = null) =>
        ProviderApiKey.Issue(providerId ?? Guid.CreateVersion7(), PlaintextKey, Now).Value;

    internal static ProviderApiKey RevokedKey()
    {
        var key = Key();
        typeof(ProviderApiKey).GetProperty(nameof(ProviderApiKey.RevokedAt))!
            .GetSetMethod(nonPublic: true)!
            .Invoke(key, [Now.AddDays(1)]);
        return key;
    }
}
