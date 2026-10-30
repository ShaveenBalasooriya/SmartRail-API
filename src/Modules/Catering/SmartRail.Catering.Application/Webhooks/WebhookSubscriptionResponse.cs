namespace SmartRail.Catering.Application.Webhooks;

/// <summary>
/// The provider's subscription as it reads it back. It names the subscription, its URL, whether it
/// is active and when it became active; the provider identifier is not repeated because a provider
/// only ever sees its own (webhook-subscriptions spec, "Manage own subscription").
/// </summary>
public sealed record WebhookSubscriptionResponse(
    Guid Id,
    string Url,
    bool IsActive,
    DateTimeOffset CreatedAt);

/// <summary>The outcome of registering a callback URL, and whether the subscription was created.</summary>
public sealed record WebhookRegistration(bool Created, WebhookSubscriptionResponse Subscription);
