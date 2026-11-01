using SmartRail.Catering.Application.Webhooks;
using SmartRail.Catering.Domain.Webhooks;

namespace SmartRail.Catering.UnitTests.TestDoubles;

/// <summary>An in-memory subscription store that records every write.</summary>
internal sealed class FakeWebhookSubscriptionRepository(WebhookSubscription? subscription = null)
    : IWebhookSubscriptionRepository
{
    public WebhookSubscription? Subscription { get; private set; } = subscription;

    public List<WebhookSubscription> Added { get; } = [];

    public int SaveChangesCount { get; private set; }

    public Exception? ExceptionToThrowOnAdd { get; set; }

    public Exception? ExceptionToThrowOnSave { get; set; }

    public Task<WebhookSubscription?> GetForProviderAsync(
        Guid providerId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Subscription?.ProviderId == providerId ? Subscription : null);

    public Task AddAsync(WebhookSubscription subscription, CancellationToken cancellationToken = default)
    {
        if (ExceptionToThrowOnAdd is not null)
        {
            throw ExceptionToThrowOnAdd;
        }

        Added.Add(subscription);
        Subscription = subscription;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Guid>> GetActiveCreatedAtOrBeforeAsync(
        DateTimeOffset recordedAt,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Guid>>(
            Subscription is { IsActive: true } && Subscription.CreatedAt <= recordedAt
                ? [Subscription.ProviderId]
                : []);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCount++;
        if (ExceptionToThrowOnSave is not null)
        {
            throw ExceptionToThrowOnSave;
        }

        return Task.CompletedTask;
    }
}
