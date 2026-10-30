namespace SmartRail.Catering.Application.Webhooks;

/// <summary>
/// One pass of the webhook dispatcher: abandon the deliveries whose subscription is no longer
/// active, then attempt the due deliveries in per-journey sequence order (design D2;
/// webhook-delivery spec, "Order preserved per journey").
/// </summary>
public interface IWebhookDeliveryPump
{
    /// <summary>Runs one pass and reports how many deliveries were attempted.</summary>
    Task<int> PumpAsync(CancellationToken cancellationToken = default);
}
