using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartRail.Audit.Application;
using SmartRail.Audit.Application.Entries;
using SmartRail.Audit.Contracts;
using SmartRail.Audit.Infrastructure.Persistence;
using SmartRail.Audit.Infrastructure.Repositories;
using SmartRail.IdentityAccess.Contracts;
using SmartRail.SharedKernel.Infrastructure;
using SmartRail.SharedKernel.Infrastructure.Persistence;

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

        services.AddDbContext<AuditDbContext>((provider, options) => options
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsAssembly(typeof(AuditDbContext).Assembly.FullName)
                .MigrationsHistoryTable("__ef_migrations_history", AuditDbContext.Schema))
            .AddRequestTransactionEnlistment(provider));

        // The append runs in the caller's request transaction, so a state change and its audit
        // entry commit or roll back together (design D1, D2).
        services.AddScoped<AuditChainWriter>();
        services.AddScoped<IAuditRecorder, AuditRecorder>();

        // The Auditor's read side reads the module's own schema through its port (design D6).
        services.AddScoped<IAuditEntryReader, AuditEntryReader>();

        // The integrity check walks the chain one entry at a time rather than loading the record.
        services.AddScoped<IAuditChainReader, AuditChainReader>();

        // Every denied write Identity announces becomes an entry here; redelivery is harmless
        // because the entry remembers the event ID (design D5).
        services.AddEventConsumer<AccessDenied, AccessDeniedConsumer>();

        return services;
    }
}
