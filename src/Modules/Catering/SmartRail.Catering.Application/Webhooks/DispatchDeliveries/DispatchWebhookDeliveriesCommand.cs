using SmartRail.Catering.Application.Webhooks;
using SmartRail.SharedKernel.Application.Messaging;
using SmartRail.SharedKernel.Results;

namespace SmartRail.Catering.Application.Webhooks.DispatchDeliveries;

/// <summary>
/// One dispatcher pass (design D2). The background job only sends this command; the work runs in its
/// handler through the pump, which holds the delivery rows with <c>FOR UPDATE SKIP LOCKED</c> while
/// it posts them.
/// </summary>
public sealed record DispatchWebhookDeliveriesCommand : ICommand<int>;

internal sealed class DispatchWebhookDeliveriesCommandHandler(IWebhookDeliveryPump pump)
    : ICommandHandler<DispatchWebhookDeliveriesCommand, int>
{
    public async Task<Result<int>> Handle(
        DispatchWebhookDeliveriesCommand command,
        CancellationToken cancellationToken) =>
        Result.Success(await pump.PumpAsync(cancellationToken));
}
