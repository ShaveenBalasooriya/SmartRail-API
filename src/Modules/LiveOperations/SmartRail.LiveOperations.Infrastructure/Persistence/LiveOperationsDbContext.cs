using Microsoft.EntityFrameworkCore;

namespace SmartRail.LiveOperations.Infrastructure.Persistence;

/// <summary>
/// The Live Operations module's database context. It is scoped to the <c>operations</c> schema and owns its own migrations history table, so no other module's migration state is
/// visible to it (design D2).
/// </summary>
/// <remarks>
/// The model is empty in the foundation change; <c>add-live-running-state</c> adds its entities.
/// </remarks>
public sealed class LiveOperationsDbContext(DbContextOptions<LiveOperationsDbContext> options) : DbContext(options)
{
    public const string Schema = "operations";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);
    }
}
