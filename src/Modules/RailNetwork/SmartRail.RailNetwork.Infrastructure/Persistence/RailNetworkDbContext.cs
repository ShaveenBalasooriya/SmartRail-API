using Microsoft.EntityFrameworkCore;

namespace SmartRail.RailNetwork.Infrastructure.Persistence;

/// <summary>
/// The Rail Network module's database context. It is scoped to the <c>network</c> schema and owns its own migrations history table, so no other module's migration state is
/// visible to it (design D2).
/// </summary>
/// <remarks>
/// The model is empty in the foundation change; <c>add-rail-network-timetable</c> adds its entities.
/// </remarks>
public sealed class RailNetworkDbContext(DbContextOptions<RailNetworkDbContext> options) : DbContext(options)
{
    public const string Schema = "network";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);
    }
}
