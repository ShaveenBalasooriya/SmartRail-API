using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartRail.Audit.Application;
using SmartRail.Audit.Infrastructure.Persistence;

namespace SmartRail.Audit.Infrastructure;

/// <summary>
/// The Audit module's Infrastructure registration point. The Host calls this and never reaches into
/// the module's internals itself.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers the module's services, including its <see cref="AuditDbContext"/>. The connection
    /// string is the one shared by every module (design D2).
    /// </summary>
    public static IServiceCollection AddAuditInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException(
                "Connection string 'Database' is not configured. Set ConnectionStrings__Database.");

        services.AddAuditApplication();

        services.AddDbContext<AuditDbContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsAssembly(typeof(AuditDbContext).Assembly.FullName)
                .MigrationsHistoryTable("__ef_migrations_history", AuditDbContext.Schema)));

        return services;
    }
}
