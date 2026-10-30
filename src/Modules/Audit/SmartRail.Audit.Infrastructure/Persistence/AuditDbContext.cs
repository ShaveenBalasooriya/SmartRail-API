using Microsoft.EntityFrameworkCore;
using SmartRail.Audit.Domain.Entries;

namespace SmartRail.Audit.Infrastructure.Persistence;

/// <summary>
/// The Audit module's database context. It is scoped to the <c>audit</c> schema and owns its own
/// migrations history table, so no other module's migration state is visible to it (design D2).
/// </summary>
/// <remarks>
/// The append-only trigger on <c>audit.entries</c> is created by the
/// <c>AddAuditEntries</c> migration rather than by the model, because EF Core cannot express it.
/// </remarks>
public sealed class AuditDbContext(DbContextOptions<AuditDbContext> options) : DbContext(options)
{
    public const string Schema = "audit";

    public DbSet<AuditEntry> Entries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AuditDbContext).Assembly);
    }
}
