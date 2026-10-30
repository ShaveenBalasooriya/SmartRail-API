using SmartRail.SharedKernel.Primitives;
using SmartRail.SharedKernel.Results;

namespace SmartRail.Catering.Domain.Webhooks;

/// <summary>
/// The callback URL a provider registered, once per provider (CAT-04). Replacing the URL replaces
/// this row rather than adding a second, so a provider never receives a change twice (design D3,
/// <c>catering.webhook_subscriptions</c>).
/// </summary>
/// <remarks>
/// <see cref="CreatedAt"/> is when the subscription most recently became active, so re-registering
/// after a deactivation does not receive the events published while it was inactive
/// (webhook-subscriptions spec, "Events from registration onward").
/// </remarks>
public sealed class WebhookSubscription : Entity<Guid>
{
    private WebhookSubscription()
    {
    }

    private WebhookSubscription(
        Guid id,
        Guid providerId,
        CallbackUrl callbackUrl,
        bool isActive,
        DateTimeOffset createdAt)
        : base(id)
    {
        ProviderId = providerId;
        CallbackUrl = callbackUrl;
        IsActive = isActive;
        CreatedAt = createdAt;
    }

    /// <summary>The provider that registered the callback.</summary>
    public Guid ProviderId { get; private set; }

    /// <summary>The absolute <c>http</c> or <c>https</c> callback URL.</summary>
    public CallbackUrl CallbackUrl { get; private set; } = null!;

    /// <summary>False once the provider deactivates or replaces the subscription.</summary>
    public bool IsActive { get; private set; }

    /// <summary>When the subscription most recently became active.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Registers a provider's callback URL (CAT-04).</summary>
    public static Result<WebhookSubscription> Create(
        Guid providerId,
        string callbackUrl,
        DateTimeOffset now)
    {
        if (providerId == Guid.Empty)
        {
            return Result.Failure<WebhookSubscription>(WebhookSubscriptionErrors.ProviderRequired);
        }

        var url = CallbackUrl.Create(callbackUrl);
        if (url.IsFailure)
        {
            return Result.Failure<WebhookSubscription>(url.Error);
        }

        return new WebhookSubscription(Guid.CreateVersion7(), providerId, url.Value, true, now);
    }

    /// <summary>
    /// Replaces the callback URL and reactivates the subscription. The provider keeps one
    /// subscription, so a replacement is an edit rather than a second row (webhook-subscriptions
    /// spec, "Replace").
    /// </summary>
    public Result Replace(string callbackUrl, DateTimeOffset now)
    {
        var url = CallbackUrl.Create(callbackUrl);
        if (url.IsFailure)
        {
            return Result.Failure(url.Error);
        }

        CallbackUrl = url.Value;
        IsActive = true;
        CreatedAt = now;

        return Result.Success();
    }

    /// <summary>Deactivates the subscription, so no further deliveries are created for it.</summary>
    public void Deactivate() => IsActive = false;
}
