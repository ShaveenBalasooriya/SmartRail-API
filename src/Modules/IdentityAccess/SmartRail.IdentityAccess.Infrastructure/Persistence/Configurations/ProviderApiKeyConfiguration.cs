using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartRail.IdentityAccess.Domain.Entities;

namespace SmartRail.IdentityAccess.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="ProviderApiKey"/> to <c>identity.provider_api_keys</c>.</summary>
internal sealed class ProviderApiKeyConfiguration : IEntityTypeConfiguration<ProviderApiKey>
{
    public void Configure(EntityTypeBuilder<ProviderApiKey> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("provider_api_keys");
        builder.HasKey(key => key.Id);

        builder.Property(key => key.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(key => key.ProviderId).HasColumnName("provider_id");
        builder.Property(key => key.KeyPrefix).HasColumnName("key_prefix").HasMaxLength(8).IsRequired();
        builder.Property(key => key.KeyHash).HasColumnName("key_hash").HasMaxLength(64).IsRequired();
        builder.Property(key => key.CreatedAt).HasColumnName("created_at");
        builder.Property(key => key.RevokedAt).HasColumnName("revoked_at");

        // The prefix is how a presented key finds its row, and it must identify exactly one key.
        builder.HasIndex(key => key.KeyPrefix).IsUnique();
    }
}
