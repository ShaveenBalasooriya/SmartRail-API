using SmartRail.Catering.Application.Webhooks;
using SmartRail.Catering.Domain.Webhooks;
using SmartRail.SharedKernel.Application.Messaging;
using SmartRail.SharedKernel.Results;

namespace SmartRail.Catering.Application.Webhooks.RegisterWebhook;

/// <summary>
/// Registers the calling provider's callback URL, replacing the one it already has (CAT-04,
/// webhook-subscriptions spec, "Register a callback URL"). The response says whether this was the
/// first registration, which is what decides 201 over 200.
/// </summary>
public sealed record RegisterWebhookCommand(Guid ProviderId, string Url) : ICommand<WebhookRegistration>;

internal sealed class RegisterWebhookCommandHandler(
    IWebhookSubscriptionRepository subscriptions,
    TimeProvider timeProvider)
    : ICommandHandler<RegisterWebhookCommand, WebhookRegistration>
{
    public async Task<Result<WebhookRegistration>> Handle(
        RegisterWebhookCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = timeProvider.GetUtcNow();
        var existing = await subscriptions.GetForProviderAsync(command.ProviderId, cancellationToken);

        if (existing is null)
        {
            var created = WebhookSubscription.Create(command.ProviderId, command.Url, now);
            if (created.IsFailure)
            {
                return Result.Failure<WebhookRegistration>(created.Error);
            }

            await subscriptions.AddAsync(created.Value, cancellationToken);
            await subscriptions.SaveChangesAsync(cancellationToken);

            return Result.Success(new WebhookRegistration(true, ToResponse(created.Value)));
        }

        var replaced = existing.Replace(command.Url, now);
        if (replaced.IsFailure)
        {
            return Result.Failure<WebhookRegistration>(replaced.Error);
        }

        await subscriptions.SaveChangesAsync(cancellationToken);

        return Result.Success(new WebhookRegistration(false, ToResponse(existing)));
    }

    private static WebhookSubscriptionResponse ToResponse(WebhookSubscription subscription) =>
        new(
            subscription.Id,
            subscription.CallbackUrl.Value,
            subscription.IsActive,
            subscription.CreatedAt);
}
