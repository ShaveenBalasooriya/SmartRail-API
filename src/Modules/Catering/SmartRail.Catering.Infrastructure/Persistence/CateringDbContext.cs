using Microsoft.EntityFrameworkCore;

namespace SmartRail.Catering.Infrastructure.Persistence;

/// <summary>
/// The Catering Integration module's database context. It is scoped to the <c>catering</c> schema and owns its own migrations history table, so no other module's migration state is
/// visible to it (design D2).
/// </summary>
/// <remarks>
/// The model is empty in the foundation change; <c>add-catering-integration</c> adds its entities.
/// </remarks>
public sealed class CateringDbContext(DbContextOptions<CateringDbContext> options) : DbContext(options)
{
    public const string Schema = "catering";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);
    }
}
