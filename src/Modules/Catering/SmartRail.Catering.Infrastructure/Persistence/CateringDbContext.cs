using Microsoft.EntityFrameworkCore;
using SmartRail.Catering.Domain.Providers;
using SmartRail.Catering.Domain.Webhooks;

namespace SmartRail.Catering.Infrastructure.Persistence;

/// <summary>
/// The Catering Integration module's database context. It is scoped to the <c>catering</c> schema and
/// owns its own migrations history table, so no other module's migration state is visible to it
/// (design D2).
/// </summary>
public sealed class CateringDbContext(DbContextOptions<CateringDbContext> options) : DbContext(options)
{
    public const string Schema = "catering";

    public DbSet<Provider> Providers => Set<Provider>();

    public DbSet<WebhookSubscription> WebhookSubscriptions => Set<WebhookSubscription>();

    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CateringDbContext).Assembly);
    }
}
