using Microsoft.EntityFrameworkCore;
using SmartRail.Catering.Application.Webhooks;
using SmartRail.Catering.Domain.Webhooks;
using SmartRail.Catering.Infrastructure.Persistence;

namespace SmartRail.Catering.Infrastructure.Webhooks;

/// <summary>
/// Reads and writes <c>catering.webhook_subscriptions</c>. A provider has at most one row, so a read
/// by provider is a single lookup and a replacement edits the row in place (design D3).
/// </summary>
internal sealed class WebhookSubscriptionRepository(CateringDbContext database)
    : IWebhookSubscriptionRepository
{
    public Task<WebhookSubscription?> GetForProviderAsync(
        Guid providerId,
        CancellationToken cancellationToken = default) =>
        database.WebhookSubscriptions.SingleOrDefaultAsync(
            subscription => subscription.ProviderId == providerId,
            cancellationToken);

    public async Task AddAsync(
        WebhookSubscription subscription,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        await database.WebhookSubscriptions.AddAsync(subscription, cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetActiveCreatedAtOrBeforeAsync(
        DateTimeOffset recordedAt,
        CancellationToken cancellationToken = default) =>
        await database.WebhookSubscriptions
            .Where(subscription => subscription.IsActive && subscription.CreatedAt <= recordedAt)
            .Select(subscription => subscription.Id)
            .ToListAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        database.SaveChangesAsync(cancellationToken);
}
