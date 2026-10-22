using System.Diagnostics.Metrics;

namespace SmartRail.SharedKernel.Infrastructure.Events;

/// <summary>
/// The delivery-failure metrics (design D10, event-delivery spec: failure visibility). The
/// OpenTelemetry exporter that ships them arrives with <c>add-critical-e2e-flows</c>; until then any
/// <see cref="MeterListener"/>, <c>dotnet-counters</c> included, reads them from the process. The
/// counters are labelled only by event type and consumer name, so nothing from a payload can reach
/// a metric.
/// </summary>
internal static class EventDeliveryMetrics
{
    /// <summary>The meter <c>dotnet-counters --counters SmartRail.Events</c> listens to.</summary>
    public const string MeterName = "SmartRail.Events";

    /// <summary>Every failed delivery attempt.</summary>
    public const string DeliveryFailuresName = "smartrail.events.delivery_failures";

    /// <summary>A delivery that reached the <c>failed</c> state after its last attempt.</summary>
    public const string DeliveriesFailedName = "smartrail.events.deliveries_failed";

    /// <summary>The event type's name, as the outbox row records it.</summary>
    public const string EventTypeTag = "event_type";

    /// <summary>The consumer's stable name (design D4).</summary>
    public const string ConsumerTag = "consumer";

    private static readonly Meter Meter = new(MeterName);

    /// <summary>Counts every failed attempt, including the fifth that fails the delivery.</summary>
    public static readonly Counter<long> DeliveryFailures = Meter.CreateCounter<long>(
        DeliveryFailuresName,
        unit: "{attempt}",
        description: "Failed attempts to deliver one event to one consumer.");

    /// <summary>Counts a delivery once, when it reaches <c>failed</c>.</summary>
    public static readonly Counter<long> DeliveriesFailed = Meter.CreateCounter<long>(
        DeliveriesFailedName,
        unit: "{delivery}",
        description: "Deliveries marked failed after their last attempt.");
}
