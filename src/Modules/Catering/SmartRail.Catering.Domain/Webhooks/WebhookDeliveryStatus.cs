namespace SmartRail.Catering.Domain.Webhooks;

/// <summary>
/// Where a webhook delivery stands. A new row is <see cref="Pending"/>; a 2xx answer makes it
/// <see cref="Delivered"/>; five failed attempts make it <see cref="Abandoned"/> and release the
/// journey's queue (webhook-delivery spec, design D2). <see cref="Failed"/> is the stored state of an
/// attempt that failed but may still be retried.
/// </summary>
public enum WebhookDeliveryStatus
{
    /// <summary>Waiting for its next attempt.</summary>
    Pending,

    /// <summary>Received by the provider.</summary>
    Delivered,

    /// <summary>An attempt failed and another is due.</summary>
    Failed,

    /// <summary>The retry limit was reached; no further attempt is made.</summary>
    Abandoned,
}
