using SmartRail.Catering.Application.Webhooks;
using SmartRail.SharedKernel.Application.Messaging;
using SmartRail.SharedKernel.Results;

namespace SmartRail.Catering.Application.Webhooks.GetWebhook;

/// <summary>
/// The calling provider's active subscription, or a not-found result when it has none
/// (webhook-subscriptions spec, "Manage own subscription").
/// </summary>
public sealed record GetWebhookQuery(Guid ProviderId) : IQuery<WebhookSubscriptionResponse>;

internal sealed class GetWebhookQueryHandler(IWebhookSubscriptionRepository subscriptions)
    : IQueryHandler<GetWebhookQuery, WebhookSubscriptionResponse>
{
    private static readonly Error NotFound =
        Error.NotFound("webhook.not_found", "This provider has no active webhook subscription.");

    public async Task<Result<WebhookSubscriptionResponse>> Handle(
        GetWebhookQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var subscription = await subscriptions.GetForProviderAsync(query.ProviderId, cancellationToken);

        if (subscription is null || !subscription.IsActive)
        {
            return Result.Failure<WebhookSubscriptionResponse>(NotFound);
        }

        return Result.Success(new WebhookSubscriptionResponse(
            subscription.Id,
            subscription.CallbackUrl.Value,
            subscription.IsActive,
            subscription.CreatedAt));
    }
}
