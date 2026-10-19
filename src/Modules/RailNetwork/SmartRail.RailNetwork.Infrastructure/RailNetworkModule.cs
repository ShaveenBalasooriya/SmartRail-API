using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartRail.RailNetwork.Infrastructure.Persistence;

namespace SmartRail.RailNetwork.Infrastructure;

/// <summary>
/// The RailNetwork module's registration point. The Host calls this and never reaches into the
/// module's internals itself.
/// </summary>
public static class RailNetworkModule
{
    /// <summary>The module's schema name.</summary>
    public const string ModuleName = "network";

    /// <summary>
    /// Registers the module's services, including its <see cref="RailNetworkDbContext"/>. The connection
    /// string is the one shared by every module (design D2).
    /// </summary>
    public static IServiceCollection AddRailNetworkModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException(
                "Connection string 'Database' is not configured. Set ConnectionStrings__Database.");

        services.AddDbContext<RailNetworkDbContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsAssembly(typeof(RailNetworkDbContext).Assembly.FullName)
                .MigrationsHistoryTable("__ef_migrations_history", RailNetworkDbContext.Schema)));

        return services;
    }
}
