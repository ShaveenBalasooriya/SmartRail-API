using Microsoft.EntityFrameworkCore;
using SmartRail.IdentityAccess.Application.Abstractions;
using SmartRail.IdentityAccess.Domain.Entities;
using SmartRail.SharedKernel.Infrastructure.Persistence;

namespace SmartRail.IdentityAccess.Infrastructure.Persistence;

/// <summary>
/// The Identity &amp; Access module's database context. It is scoped to the <c>identity</c> schema
/// and owns its own migrations history table, so no other module's migration state is visible to it
/// (design D2). It is also the module's unit of work and the owner of its event-bus outbox.
/// </summary>
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options)
    : DbContext(options), IUnitOfWork
{
    public const string Schema = "identity";

    public DbSet<StaffMember> StaffMembers => Set<StaffMember>();

    public DbSet<ProviderApiKey> ProviderApiKeys => Set<ProviderApiKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);
        modelBuilder.AddOutbox();
    }
}
