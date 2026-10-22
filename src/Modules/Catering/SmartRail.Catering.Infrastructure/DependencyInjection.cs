using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartRail.Catering.Application;
using SmartRail.Catering.Infrastructure.Persistence;

namespace SmartRail.Catering.Infrastructure;

/// <summary>
/// The Catering module's Infrastructure registration point. The Host calls this and never reaches
/// into the module's internals itself.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers the module's services, including its <see cref="CateringDbContext"/>. The connection
    /// string is the one shared by every module (design D2).
    /// </summary>
    public static IServiceCollection AddCateringInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException(
                "Connection string 'Database' is not configured. Set ConnectionStrings__Database.");

        services.AddCateringApplication();

        services.AddDbContext<CateringDbContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsAssembly(typeof(CateringDbContext).Assembly.FullName)
                .MigrationsHistoryTable("__ef_migrations_history", CateringDbContext.Schema)));

        return services;
    }
}
