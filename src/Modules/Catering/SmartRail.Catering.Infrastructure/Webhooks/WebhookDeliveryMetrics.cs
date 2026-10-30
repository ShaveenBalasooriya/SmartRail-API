using System.Diagnostics.Metrics;

namespace SmartRail.Catering.Infrastructure.Webhooks;

/// <summary>
/// The webhook delivery metrics (design D7, S-19, EP-17): attempts, failures, abandoned deliveries
/// and the publish-to-delivery latency. The OpenTelemetry setup added by
/// <c>add-critical-e2e-flows</c> exports them; nothing here depends on it.
/// </summary>
internal static class WebhookDeliveryMetrics
{
    /// <summary>The meter every webhook metric belongs to.</summary>
    public const string MeterName = "SmartRail.Catering";

    private static readonly Meter Meter = new(MeterName);

    /// <summary>One increment per POST attempted, whether it succeeded or not.</summary>
    public static readonly Counter<long> Attempts = Meter.CreateCounter<long>(
        "smartrail.webhook.attempts",
        unit: "{attempt}",
        description: "Webhook delivery attempts, including retries.");

    /// <summary>One increment per attempt that was not answered with 2xx.</summary>
    public static readonly Counter<long> Failures = Meter.CreateCounter<long>(
        "smartrail.webhook.failures",
        unit: "{failure}",
        description: "Webhook delivery attempts that failed.");

    /// <summary>One increment per delivery abandoned, by the retry limit or a deactivated subscription.</summary>
    public static readonly Counter<long> Abandoned = Meter.CreateCounter<long>(
        "smartrail.webhook.abandoned",
        unit: "{delivery}",
        description: "Webhook deliveries abandoned without being received.");

    /// <summary>How long a delivery waited between its creation and the receiver's 2xx answer.</summary>
    public static readonly Histogram<double> DeliveryLatency = Meter.CreateHistogram<double>(
        "smartrail.webhook.delivery_latency",
        unit: "s",
        description: "Seconds between a delivery record's creation and its successful delivery.");
}
