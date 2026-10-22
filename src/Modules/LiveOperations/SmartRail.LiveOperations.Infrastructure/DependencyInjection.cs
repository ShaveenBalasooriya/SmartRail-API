using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartRail.LiveOperations.Application;
using SmartRail.LiveOperations.Application.Abstractions;
using SmartRail.LiveOperations.Application.JourneyEvents;
using SmartRail.LiveOperations.Application.LiveJourneys;
using SmartRail.LiveOperations.Application.LiveState;
using SmartRail.LiveOperations.Application.LiveState.CreateLiveState;
using SmartRail.LiveOperations.Infrastructure.Jobs;
using SmartRail.LiveOperations.Infrastructure.Persistence;
using SmartRail.LiveOperations.Infrastructure.Repositories;
using SmartRail.RailNetwork.Contracts;
using SmartRail.SharedKernel.Application.Abstractions;
using SmartRail.SharedKernel.Application.Events;
using SmartRail.SharedKernel.Infrastructure;
using SmartRail.SharedKernel.Infrastructure.Events;

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

        // The context owns the module's outbox, so its save wakes the dispatcher (design D7).
        services.AddDbContext<LiveOperationsDbContext>((provider, options) => options
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsAssembly(typeof(LiveOperationsDbContext).Assembly.FullName)
                .MigrationsHistoryTable("__ef_migrations_history", LiveOperationsDbContext.Schema))
            .AddOutboxSignal(provider));

        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<LiveOperationsDbContext>());

        // The module publishes its cross-module events through its own outbox (design D1, D9).
        services.AddIntegrationEventPublisher<LiveOperationsDbContext>(IntegrationEventPublisherKeys.Operations);
        services.AddOutboxSource<LiveOperationsDbContext>(IntegrationEventPublisherKeys.Operations);

        services.AddScoped<ILiveStateRepository, LiveStateRepository>();
        services.AddScoped<ILiveJourneyRepository, LiveJourneyRepository>();
        services.AddScoped<IJourneyEventRepository, JourneyEventRepository>();

        // The real reader arrives with the departure-log and status-change changes; TryAdd lets
        // them replace this one without touching the module's registration.
        services.TryAddScoped<IJourneyFactReader, EmptyJourneyFactReader>();

        // Rail Network's journey announcements create this module's live state (design D4, D9).
        services.AddEventConsumer<JourneyGenerated, JourneyGeneratedHandler>();

        services.AddHostedService<LiveStateBackfillJob>();

        return services;
    }
}
