using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SmartRail.IdentityAccess.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef</c> build the model without starting the Host. It reads the same
/// <c>ConnectionStrings__Database</c> value the API uses, falling back to a local default so that
/// <c>migrations add</c> works on a fresh clone without any configuration.
/// </summary>
public sealed class IdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Database")
            ?? "Host=localhost;Port=5432;Database=smartrail;Username=smartrail;Password=smartrail";

        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsAssembly(typeof(IdentityDbContext).Assembly.FullName)
                .MigrationsHistoryTable("__ef_migrations_history", IdentityDbContext.Schema))
            .Options;

        return new IdentityDbContext(options);
    }
}
