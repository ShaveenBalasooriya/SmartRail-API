using Microsoft.EntityFrameworkCore;

namespace SmartRail.SharedKernel.Infrastructure.Persistence;

/// <summary>
/// Maps the event bus's two tables into the calling module's own schema (design D2, D5). A module's
/// <c>DbContext</c> calls this after it has set its default schema, so its outbox lives beside its
/// own tables and its rows are saved in the same transaction as the change that recorded them.
/// </summary>
public static class OutboxModelBuilderExtensions
{
    /// <summary>Adds <c>outbox_messages</c> and <c>outbox_deliveries</c> to this model.</summary>
    public static ModelBuilder AddOutbox(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<OutboxMessage>(message =>
        {
            message.ToTable("outbox_messages");

            message.HasKey(row => row.Id);

            message.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            message.Property(row => row.Position).HasColumnName("position").UseIdentityByDefaultColumn();
            message.Property(row => row.EventType).HasColumnName("event_type").IsRequired();
            message.Property(row => row.OrderingKey).HasColumnName("ordering_key");
            message.Property(row => row.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
            message.Property(row => row.OccurredAt).HasColumnName("occurred_at");
            message.Property(row => row.PublishedAt).HasColumnName("published_at");
        });

        modelBuilder.Entity<OutboxDelivery>(delivery =>
        {
            delivery.ToTable("outbox_deliveries");

            // One delivery per message and consumer: the unique pair is also what the transport's
            // ON CONFLICT DO NOTHING needs, so publishing the same message twice is harmless.
            delivery.HasKey(row => new { row.MessageId, row.Consumer });

            delivery.Property(row => row.MessageId).HasColumnName("message_id").ValueGeneratedNever();
            delivery.Property(row => row.Consumer).HasColumnName("consumer");
            delivery.Property(row => row.Status).HasColumnName("status").HasConversion(
                    status => ToStorage(status),
                    value => FromStorage(value))
                .IsRequired();
            delivery.Property(row => row.AttemptCount).HasColumnName("attempt_count");
            delivery.Property(row => row.NextAttemptAt).HasColumnName("next_attempt_at");
            delivery.Property(row => row.LastAttemptAt).HasColumnName("last_attempt_at");
            delivery.Property(row => row.DeliveredAt).HasColumnName("delivered_at");
        });

        return modelBuilder;
    }

    private static string ToStorage(OutboxDeliveryStatus status) => status switch
    {
        OutboxDeliveryStatus.Pending => "pending",
        OutboxDeliveryStatus.Delivered => "delivered",
        OutboxDeliveryStatus.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown delivery status."),
    };

    private static OutboxDeliveryStatus FromStorage(string value) => value switch
    {
        "pending" => OutboxDeliveryStatus.Pending,
        "delivered" => OutboxDeliveryStatus.Delivered,
        "failed" => OutboxDeliveryStatus.Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown delivery status."),
    };
}
