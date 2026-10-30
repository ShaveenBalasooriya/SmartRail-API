using SmartRail.SharedKernel.Primitives;

namespace SmartRail.Catering.Domain.Webhooks;

/// <summary>
/// One delivery of one pushed event to one subscription, unique on
/// (<see cref="SubscriptionId"/>, <see cref="EventId"/>). The unique index is what makes a redelivered
/// bus message harmless (CAT-06); <see cref="AttemptCount"/> and <see cref="NextAttemptAt"/> drive the
/// retry schedule (CAT-08), and <see cref="SequenceNumber"/> carries the journey's ordering
/// (CAT-07). The row is created by Catering's consumer of <c>JourneyStateChanged</c> (design D1,
/// <c>catering.webhook_deliveries</c>).
/// </summary>
public sealed class WebhookDelivery : Entity<Guid>
{
    private WebhookDelivery()
    {
    }

    /// <summary>The subscription that receives the delivery.</summary>
    public Guid SubscriptionId { get; private set; }

    /// <summary>The journey event's identifier, which is the event's identifier on the bus.</summary>
    public Guid EventId { get; private set; }

    /// <summary>The journey the event belongs to.</summary>
    public Guid JourneyId { get; private set; }

    /// <summary>The per-journey sequence number, which may skip numbers for events that are not pushed.</summary>
    public long SequenceNumber { get; private set; }

    /// <summary>One of the five pushed event types.</summary>
    public WebhookEventType EventType { get; private set; }

    /// <summary>The provider payload, stored as JSONB before the delivery is sent.</summary>
    public string Payload { get; private set; } = null!;

    /// <summary>Where the delivery stands in its retry schedule.</summary>
    public WebhookDeliveryStatus Status { get; private set; }

    /// <summary>How many attempts have been made; five failures abandon the delivery.</summary>
    public int AttemptCount { get; private set; }

    /// <summary>When the next attempt becomes due.</summary>
    public DateTimeOffset NextAttemptAt { get; private set; }

    /// <summary>When the last attempt was made, or <see langword="null"/> if there has been none.</summary>
    public DateTimeOffset? LastAttemptAt { get; private set; }

    /// <summary>The response status of the last attempt, or <see langword="null"/> on a network error.</summary>
    public int? LastResponseStatus { get; private set; }

    /// <summary>When the receiver answered 2xx.</summary>
    public DateTimeOffset? DeliveredAt { get; private set; }

    /// <summary>When the delivery row was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }
}
