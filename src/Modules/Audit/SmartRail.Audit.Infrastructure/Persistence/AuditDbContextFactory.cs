using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SmartRail.Audit.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef</c> build the model without starting the Host. It reads the same
/// <c>ConnectionStrings__Database</c> value the API uses, falling back to a local default so that
/// <c>migrations add</c> works on a fresh clone without any configuration.
/// </summary>
public sealed class AuditDbContextFactory : IDesignTimeDbContextFactory<AuditDbContext>
{
    public AuditDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Database")
            ?? "Host=localhost;Port=5432;Database=smartrail;Username=smartrail;Password=smartrail";

        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsAssembly(typeof(AuditDbContext).Assembly.FullName)
                .MigrationsHistoryTable("__ef_migrations_history", AuditDbContext.Schema))
            .Options;

        return new AuditDbContext(options);
    }
}
