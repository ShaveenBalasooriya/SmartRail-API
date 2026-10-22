using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartRail.LiveOperations.Application;
using SmartRail.LiveOperations.Application.Abstractions;
using SmartRail.LiveOperations.Application.JourneyEvents;
using SmartRail.LiveOperations.Application.LiveJourneys;
using SmartRail.LiveOperations.Application.LiveState;
using SmartRail.LiveOperations.Infrastructure.Jobs;
using SmartRail.LiveOperations.Infrastructure.Persistence;
using SmartRail.LiveOperations.Infrastructure.Repositories;

namespace SmartRail.LiveOperations.Infrastructure;

/// <summary>
/// The Live Operations module's Infrastructure registration point. The Host calls this and never
/// reaches into the module's internals itself.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers the module's services, including its <see cref="LiveOperationsDbContext"/>. The
    /// connection string is the one shared by every module (design D2).
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

        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<LiveOperationsDbContext>());
        services.AddScoped<ILiveStateRepository, LiveStateRepository>();
        services.AddScoped<ILiveJourneyRepository, LiveJourneyRepository>();
        services.AddScoped<IJourneyEventRepository, JourneyEventRepository>();

        // The real reader arrives with the departure-log and status-change changes; TryAdd lets
        // them replace this one without touching the module's registration.
        services.TryAddScoped<IJourneyFactReader, EmptyJourneyFactReader>();

        services.AddHostedService<LiveStateBackfillJob>();

        return services;
    }
}
