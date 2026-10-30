using SmartRail.SharedKernel.Results;

namespace SmartRail.Catering.Domain.Webhooks;

/// <summary>The expected failures of creating a <see cref="WebhookSubscription"/>.</summary>
public static class WebhookSubscriptionErrors
{
    /// <summary>A subscription belongs to one provider, so the identifier is required.</summary>
    public static readonly Error ProviderRequired =
        Error.Validation("webhook_subscription.provider_id", "A provider identifier is required.");
}
