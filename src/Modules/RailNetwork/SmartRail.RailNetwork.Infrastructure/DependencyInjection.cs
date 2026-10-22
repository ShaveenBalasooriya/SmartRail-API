using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartRail.RailNetwork.Application;
using SmartRail.RailNetwork.Application.Abstractions;
using SmartRail.RailNetwork.Application.Journeys;
using SmartRail.RailNetwork.Application.Network;
using SmartRail.RailNetwork.Application.Timetable;
using SmartRail.RailNetwork.Domain.Abstractions;
using SmartRail.RailNetwork.Infrastructure.Clock;
using SmartRail.RailNetwork.Infrastructure.Jobs;
using SmartRail.RailNetwork.Infrastructure.Persistence;
using SmartRail.RailNetwork.Infrastructure.Repositories;
using SmartRail.RailNetwork.Infrastructure.Seeding;
using SmartRail.SharedKernel.Application.Abstractions;
using SmartRail.SharedKernel.Application.Events;
using SmartRail.SharedKernel.Infrastructure;
using SmartRail.SharedKernel.Infrastructure.Events;

namespace SmartRail.RailNetwork.Infrastructure;

/// <summary>
/// The Rail Network module's Infrastructure registration point. The Host calls this and never
/// reaches into the module's internals itself.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers the module's services, including its <see cref="RailNetworkDbContext"/>. The
    /// connection string is the one shared by every module (design D2).
    /// </summary>
    public static IServiceCollection AddRailNetworkInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException(
                "Connection string 'Database' is not configured. Set ConnectionStrings__Database.");

        services.AddRailNetworkApplication();

        services.AddDbContext<RailNetworkDbContext>((provider, options) => options
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsAssembly(typeof(RailNetworkDbContext).Assembly.FullName)
                .MigrationsHistoryTable("__ef_migrations_history", RailNetworkDbContext.Schema))
            .AddOutboxSignal(provider));

        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<RailNetworkDbContext>());
        services.AddScoped<INetworkRepository, NetworkRepository>();
        services.AddScoped<ITimetableRepository, TimetableRepository>();
        services.AddScoped<IJourneyRepository, JourneyRepository>();

        // The Host runs every IModuleSeeder after migrations; this module owns only its own schema.
        services.AddScoped<IModuleSeeder, RailNetworkSeeder>();

        // The module publishes its cross-module events through its own outbox (design D1, D9).
        services.AddIntegrationEventPublisher<RailNetworkDbContext>(IntegrationEventPublisherKeys.Network);
        services.AddOutboxSource<RailNetworkDbContext>(IntegrationEventPublisherKeys.Network);

        // The railway's time zone is configured once here; everything else depends on the port.
        var configuredTimeZoneId = configuration["Railway:TimeZone"];
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(
            string.IsNullOrWhiteSpace(configuredTimeZoneId)
                ? RailwayClock.DefaultTimeZoneId
                : configuredTimeZoneId);

        services.AddSingleton<IRailwayClock>(provider => new RailwayClock(
            provider.GetRequiredService<TimeProvider>(),
            timeZone));

        services.AddHostedService<JourneyGenerationJob>();

        return services;
    }
}
