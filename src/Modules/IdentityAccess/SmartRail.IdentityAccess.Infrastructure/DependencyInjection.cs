using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartRail.IdentityAccess.Application;
using SmartRail.IdentityAccess.Infrastructure.Persistence;

namespace SmartRail.IdentityAccess.Infrastructure;

/// <summary>
/// The Identity &amp; Access module's Infrastructure registration point. The Host calls this and
/// never reaches into the module's internals itself.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers the module's services, including its <see cref="IdentityDbContext"/>. The
    /// connection string is the one shared by every module (design D2).
    /// </summary>
    public static IServiceCollection AddIdentityAccessInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException(
                "Connection string 'Database' is not configured. Set ConnectionStrings__Database.");

        services.AddIdentityAccessApplication();

        services.AddDbContext<IdentityDbContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsAssembly(typeof(IdentityDbContext).Assembly.FullName)
                .MigrationsHistoryTable("__ef_migrations_history", IdentityDbContext.Schema)));

        return services;
    }
}
