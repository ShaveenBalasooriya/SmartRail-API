using SmartRail.Catering.Domain.Webhooks;

namespace SmartRail.Catering.Application.Webhooks;

/// <summary>
/// The webhook subscription store. A provider has at most one subscription, so every read is by
/// provider and a registration edits the row it finds (webhook-subscriptions spec, design D3).
/// </summary>
public interface IWebhookSubscriptionRepository
{
    /// <summary>
    /// The provider's subscription whatever its state, or <see langword="null"/> when it has never
    /// registered.
    /// </summary>
    Task<WebhookSubscription?> GetForProviderAsync(
        Guid providerId,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a provider's first subscription.</summary>
    Task AddAsync(WebhookSubscription subscription, CancellationToken cancellationToken = default);

    /// <summary>
    /// The active subscriptions that existed when <paramref name="recordedAt"/> happened, which are
    /// the ones that receive an event recorded then (webhook-delivery consumer, design D1).
    /// </summary>
    Task<IReadOnlyList<Guid>> GetActiveCreatedAtOrBeforeAsync(
        DateTimeOffset recordedAt,
        CancellationToken cancellationToken = default);

    /// <summary>Writes the change made to a subscription.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
