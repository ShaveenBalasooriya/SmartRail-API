using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartRail.Catering.Domain.Providers;
using SmartRail.Catering.Domain.Webhooks;

namespace SmartRail.Catering.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="WebhookSubscription"/> to <c>catering.webhook_subscriptions</c>.</summary>
internal sealed class WebhookSubscriptionConfiguration : IEntityTypeConfiguration<WebhookSubscription>
{
    public void Configure(EntityTypeBuilder<WebhookSubscription> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("webhook_subscriptions");
        builder.HasKey(subscription => subscription.Id);

        builder.Property(subscription => subscription.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(subscription => subscription.ProviderId).HasColumnName("provider_id");
        builder
            .Property(subscription => subscription.CallbackUrl)
            .HasColumnName("callback_url")
            .HasMaxLength(CallbackUrl.MaxLength)
            .HasConversion(url => url.Value, value => CallbackUrl.FromStorage(value))
            .IsRequired();
        builder.Property(subscription => subscription.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(subscription => subscription.CreatedAt).HasColumnName("created_at");

        // A provider has one subscription; a second registration replaces the first rather than
        // adding a row (webhook-subscriptions spec, "Register a callback URL").
        builder.HasIndex(subscription => subscription.ProviderId).IsUnique();

        // Both ends live in the catering schema, so the reference is a real foreign key (data model).
        builder
            .HasOne<Provider>()
            .WithMany()
            .HasForeignKey(subscription => subscription.ProviderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
