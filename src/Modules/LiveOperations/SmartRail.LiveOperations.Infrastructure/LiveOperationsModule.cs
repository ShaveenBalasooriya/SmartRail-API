using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartRail.LiveOperations.Infrastructure.Persistence;

namespace SmartRail.LiveOperations.Infrastructure;

/// <summary>
/// The LiveOperations module's registration point. The Host calls this and never reaches into the
/// module's internals itself.
/// </summary>
public static class LiveOperationsModule
{
    /// <summary>The module's schema name.</summary>
    public const string ModuleName = "operations";

    /// <summary>
    /// Registers the module's services, including its <see cref="LiveOperationsDbContext"/>. The connection
    /// string is the one shared by every module (design D2).
    /// </summary>
    public static IServiceCollection AddLiveOperationsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException(
                "Connection string 'Database' is not configured. Set ConnectionStrings__Database.");

        services.AddDbContext<LiveOperationsDbContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsAssembly(typeof(LiveOperationsDbContext).Assembly.FullName)
                .MigrationsHistoryTable("__ef_migrations_history", LiveOperationsDbContext.Schema)));

        return services;
    }
}
