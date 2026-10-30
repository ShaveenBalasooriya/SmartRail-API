using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartRail.Catering.Domain.Providers;

namespace SmartRail.Catering.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="Provider"/> to <c>catering.providers</c>.</summary>
internal sealed class ProviderConfiguration : IEntityTypeConfiguration<Provider>
{
    public void Configure(EntityTypeBuilder<Provider> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("providers");
        builder.HasKey(provider => provider.Id);

        builder.Property(provider => provider.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(provider => provider.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(provider => provider.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(provider => provider.CreatedAt).HasColumnName("created_at");
    }
}
