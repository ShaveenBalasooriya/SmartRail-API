using SmartRail.Catering.Application.Webhooks;
using SmartRail.SharedKernel.Application.Messaging;
using SmartRail.SharedKernel.Results;

namespace SmartRail.Catering.Application.Webhooks.DeactivateWebhook;

/// <summary>
/// Deactivates the calling provider's subscription, so no further deliveries are created for it
/// (webhook-subscriptions spec, "Deactivate").
/// </summary>
public sealed record DeactivateWebhookCommand(Guid ProviderId) : ICommand;

internal sealed class DeactivateWebhookCommandHandler(IWebhookSubscriptionRepository subscriptions)
    : ICommandHandler<DeactivateWebhookCommand>
{
    private static readonly Error NotFound =
        Error.NotFound("webhook.not_found", "This provider has no active webhook subscription.");

    public async Task<Result> Handle(
        DeactivateWebhookCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var subscription = await subscriptions.GetForProviderAsync(command.ProviderId, cancellationToken);

        if (subscription is null || !subscription.IsActive)
        {
            return Result.Failure(NotFound);
        }

        subscription.Deactivate();
        await subscriptions.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
