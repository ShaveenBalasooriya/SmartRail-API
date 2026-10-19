using Microsoft.EntityFrameworkCore;

namespace SmartRail.IdentityAccess.Infrastructure.Persistence;

/// <summary>
/// The Identity &amp; Access module's database context. It is scoped to the <c>identity</c> schema
/// and owns its own migrations history table, so no other module's migration state is visible to it
/// (design D2).
/// </summary>
/// <remarks>
/// The model is empty in the foundation change; <c>add-identity-access</c> adds its entities.
/// </remarks>
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public const string Schema = "identity";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);
    }
}
