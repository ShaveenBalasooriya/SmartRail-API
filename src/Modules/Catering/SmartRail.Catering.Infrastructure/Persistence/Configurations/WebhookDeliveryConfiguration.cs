using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartRail.Catering.Domain.Webhooks;

namespace SmartRail.Catering.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="WebhookDelivery"/> to <c>catering.webhook_deliveries</c>.</summary>
internal sealed class WebhookDeliveryConfiguration : IEntityTypeConfiguration<WebhookDelivery>
{
    public void Configure(EntityTypeBuilder<WebhookDelivery> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "webhook_deliveries",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_webhook_deliveries_event_type",
                    "event_type IN ('delay_published','cancellation_published','termination_published','platform_changed','correction_published')");
                table.HasCheckConstraint(
                    "CK_webhook_deliveries_status",
                    "status IN ('pending','delivered','failed','abandoned')");
            });

        builder.HasKey(delivery => delivery.Id);

        builder.Property(delivery => delivery.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(delivery => delivery.SubscriptionId).HasColumnName("subscription_id");
        builder.Property(delivery => delivery.EventId).HasColumnName("event_id");
        builder.Property(delivery => delivery.JourneyId).HasColumnName("journey_id");
        builder.Property(delivery => delivery.SequenceNumber).HasColumnName("sequence_number");
        builder
            .Property(delivery => delivery.EventType)
            .HasColumnName("event_type")
            .HasConversion(
                eventType => EventTypeToStorage(eventType),
                value => EventTypeFromStorage(value))
            .IsRequired();
        builder.Property(delivery => delivery.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        builder
            .Property(delivery => delivery.Status)
            .HasColumnName("status")
            .HasConversion(
                status => StatusToStorage(status),
                value => StatusFromStorage(value))
            .IsRequired();
        builder.Property(delivery => delivery.AttemptCount).HasColumnName("attempt_count");
        builder.Property(delivery => delivery.NextAttemptAt).HasColumnName("next_attempt_at");
        builder.Property(delivery => delivery.LastAttemptAt).HasColumnName("last_attempt_at");
        builder.Property(delivery => delivery.LastResponseStatus).HasColumnName("last_response_status");
        builder.Property(delivery => delivery.DeliveredAt).HasColumnName("delivered_at");
        builder.Property(delivery => delivery.CreatedAt).HasColumnName("created_at");

        // One row per event per subscription, however many times the bus delivers the message
        // (webhook-delivery spec, CAT-06).
        builder.HasIndex(delivery => new { delivery.SubscriptionId, delivery.EventId }).IsUnique();

        builder
            .HasOne<WebhookSubscription>()
            .WithMany()
            .HasForeignKey(delivery => delivery.SubscriptionId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static string EventTypeToStorage(WebhookEventType eventType) => eventType switch
    {
        WebhookEventType.DelayPublished => "delay_published",
        WebhookEventType.CancellationPublished => "cancellation_published",
        WebhookEventType.TerminationPublished => "termination_published",
        WebhookEventType.PlatformChanged => "platform_changed",
        WebhookEventType.CorrectionPublished => "correction_published",
        _ => throw new ArgumentOutOfRangeException(nameof(eventType), eventType, "Unknown webhook event type."),
    };

    private static WebhookEventType EventTypeFromStorage(string value) => value switch
    {
        "delay_published" => WebhookEventType.DelayPublished,
        "cancellation_published" => WebhookEventType.CancellationPublished,
        "termination_published" => WebhookEventType.TerminationPublished,
        "platform_changed" => WebhookEventType.PlatformChanged,
        "correction_published" => WebhookEventType.CorrectionPublished,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown webhook event type."),
    };

    private static string StatusToStorage(WebhookDeliveryStatus status) => status switch
    {
        WebhookDeliveryStatus.Pending => "pending",
        WebhookDeliveryStatus.Delivered => "delivered",
        WebhookDeliveryStatus.Failed => "failed",
        WebhookDeliveryStatus.Abandoned => "abandoned",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown webhook delivery status."),
    };

    private static WebhookDeliveryStatus StatusFromStorage(string value) => value switch
    {
        "pending" => WebhookDeliveryStatus.Pending,
        "delivered" => WebhookDeliveryStatus.Delivered,
        "failed" => WebhookDeliveryStatus.Failed,
        "abandoned" => WebhookDeliveryStatus.Abandoned,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown webhook delivery status."),
    };
}
