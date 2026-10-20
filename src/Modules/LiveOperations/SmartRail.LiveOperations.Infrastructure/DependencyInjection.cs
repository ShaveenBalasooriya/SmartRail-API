using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartRail.LiveOperations.Application;
using SmartRail.LiveOperations.Infrastructure.Persistence;

namespace SmartRail.LiveOperations.Infrastructure;

/// <summary>
/// The Live Operations module's Infrastructure registration point. The Host calls this and never reaches into
/// the module's internals itself.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers the module's services, including its <see cref="LiveOperationsDbContext"/>. The connection
    /// string is the one shared by every module (design D2).
    /// </summary>
    public static IServiceCollection AddLiveOperationsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException(
                "Connection string 'Database' is not configured. Set ConnectionStrings__Database.");

        services.AddLiveOperationsApplication();

        services.AddDbContext<LiveOperationsDbContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsAssembly(typeof(LiveOperationsDbContext).Assembly.FullName)
                .MigrationsHistoryTable("__ef_migrations_history", LiveOperationsDbContext.Schema)));

        return services;
    }
}
