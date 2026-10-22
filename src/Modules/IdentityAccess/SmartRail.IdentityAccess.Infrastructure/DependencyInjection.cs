using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartRail.IdentityAccess.Application;
using SmartRail.IdentityAccess.Application.Abstractions;
using SmartRail.IdentityAccess.Application.ProviderKeys;
using SmartRail.IdentityAccess.Application.Staff;
using SmartRail.IdentityAccess.Infrastructure.Persistence;
using SmartRail.IdentityAccess.Infrastructure.Repositories;
using SmartRail.IdentityAccess.Infrastructure.Seeding;
using SmartRail.SharedKernel.Application.Abstractions;
using SmartRail.SharedKernel.Application.Events;
using SmartRail.SharedKernel.Infrastructure;
using SmartRail.SharedKernel.Infrastructure.Events;

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

        // The context owns the module's outbox, so its save wakes the dispatcher (design D7).
        services.AddDbContext<IdentityDbContext>((provider, options) => options
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsAssembly(typeof(IdentityDbContext).Assembly.FullName)
                .MigrationsHistoryTable("__ef_migrations_history", IdentityDbContext.Schema))
            .AddOutboxSignal(provider));

        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<IdentityDbContext>());

        // The module publishes its cross-module events through its own outbox (design D1, D9).
        services.AddIntegrationEventPublisher<IdentityDbContext>(IntegrationEventPublisherKeys.Identity);
        services.AddOutboxSource<IdentityDbContext>(IntegrationEventPublisherKeys.Identity);

        services.AddScoped<IStaffMemberRepository, StaffMemberRepository>();
        services.AddScoped<IProviderApiKeyRepository, ProviderApiKeyRepository>();

        // The Host runs every IModuleSeeder after migrations; this module owns only its own schema.
        services.AddScoped<IModuleSeeder, IdentitySeeder>();

        return services;
    }
}
