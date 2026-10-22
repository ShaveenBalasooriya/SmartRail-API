namespace SmartRail.SharedKernel.Application.Events;

/// <summary>
/// The keys a module's <see cref="IIntegrationEventPublisher"/> is registered under (design D1): one
/// per publishing module, so a handler resolves the publisher backed by its own module's
/// <c>DbContext</c>. Each value is the module's schema name.
/// </summary>
public static class IntegrationEventPublisherKeys
{
    /// <summary>The Identity &amp; Access module's publisher (<c>identity</c> schema).</summary>
    public const string Identity = "identity";

    /// <summary>The Rail Network module's publisher (<c>network</c> schema).</summary>
    public const string Network = "network";

    /// <summary>The Live Operations module's publisher (<c>operations</c> schema).</summary>
    public const string Operations = "operations";
}
