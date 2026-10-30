using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartRail.LiveOperations.Application;
using SmartRail.LiveOperations.Application.Abstractions;
using SmartRail.LiveOperations.Application.ControllerView;
using SmartRail.LiveOperations.Application.Departures;
using SmartRail.LiveOperations.Application.JourneyEvents;
using SmartRail.LiveOperations.Application.LiveJourneys;
using SmartRail.LiveOperations.Application.LiveState;
using SmartRail.LiveOperations.Application.LiveState.CreateLiveState;
using SmartRail.LiveOperations.Application.Publishing;
using SmartRail.LiveOperations.Infrastructure.Clock;
using SmartRail.LiveOperations.Infrastructure.Jobs;
using SmartRail.LiveOperations.Infrastructure.Persistence;
using SmartRail.LiveOperations.Infrastructure.Repositories;
using SmartRail.RailNetwork.Contracts;
using SmartRail.SharedKernel.Application.Abstractions;
using SmartRail.SharedKernel.Application.Events;
using SmartRail.SharedKernel.Infrastructure;
using SmartRail.SharedKernel.Infrastructure.Events;
using SmartRail.SharedKernel.Infrastructure.Persistence;

namespace SmartRail.LiveOperations.Infrastructure;

/// <summary>
/// The Live Operations module's Infrastructure registration point. The Host calls this and never
/// reaches into the module's internals itself.
/// </summary>
public static class DependencyInjection
{
    /// <summary>The railway's time zone when <c>Railway:TimeZone</c> is not configured; the Rail Network uses the same default.</summary>
    private const string DefaultTimeZoneId = "Asia/Colombo";

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

        services.AddSingleton(ReadOperationsOptions(configuration));

        // The railway's calendar day is read in the same configured zone the Rail Network uses.
        var configuredTimeZoneId = configuration["Railway:TimeZone"];
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(
            string.IsNullOrWhiteSpace(configuredTimeZoneId) ? DefaultTimeZoneId : configuredTimeZoneId);
        services.AddSingleton<IRailwayCalendar>(new RailwayCalendar(timeZone));

        // The context owns the module's outbox, so its save wakes the dispatcher (design D7).
        services.AddDbContext<LiveOperationsDbContext>((provider, options) => options
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsAssembly(typeof(LiveOperationsDbContext).Assembly.FullName)
                .MigrationsHistoryTable("__ef_migrations_history", LiveOperationsDbContext.Schema))
            .AddOutboxSignal(provider)
            .AddRequestTransactionEnlistment(provider));

        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<LiveOperationsDbContext>());

        // The module publishes its cross-module events through its own outbox (design D1, D9).
        services.AddIntegrationEventPublisher<LiveOperationsDbContext>(IntegrationEventPublisherKeys.Operations);
        services.AddOutboxSource<LiveOperationsDbContext>(IntegrationEventPublisherKeys.Operations);

        services.AddScoped<ILiveStateRepository, LiveStateRepository>();
        services.AddScoped<ILiveJourneyRepository, LiveJourneyRepository>();
        services.AddScoped<IJourneyEventRepository, JourneyEventRepository>();

        // The facts a journey's state is computed from: the controller's published changes now,
        // the Guard's departure logs from add-departure-logging.
        services.AddScoped<IJourneyFactReader, JourneyFactReader>();

        // The changes published on a journey, for the controller's change history.
        services.AddScoped<IStatusChangeReader, StatusChangeReader>();

        // The write port the publishing pipeline stores a change through (design D2).
        services.AddScoped<IStatusChangeRepository, StatusChangeRepository>();

        // The write port the Guard's departure logging stores a log through (departure-logging D3).
        services.AddScoped<IDepartureLogRepository, DepartureLogRepository>();

        // Rail Network's journey announcements create this module's live state (design D4, D9).
        services.AddEventConsumer<JourneyGenerated, JourneyGeneratedHandler>();

        services.AddHostedService<LiveStateBackfillJob>();

        // The passing of time is not a recorded fact, so a periodic check is what completes a
        // journey once its terminus arrival has passed (design D5).
        services.AddHostedService<JourneyCompletionJob>();

        return services;
    }

    /// <summary>
    /// Reads <c>Operations:StaleAfterMinutes</c> and <c>Operations:CompletionCheckSeconds</c>,
    /// defaulting to ten minutes and sixty seconds. A value that is not a positive whole number stops
    /// start-up rather than quietly changing when journeys look stale or when they complete.
    /// </summary>
    private static OperationsOptions ReadOperationsOptions(IConfiguration configuration)
    {
        var staleAfterMinutes = ReadPositiveWholeNumber(
            configuration,
            "Operations:StaleAfterMinutes",
            "minutes",
            OperationsOptions.DefaultStaleAfterMinutes);

        var completionCheckSeconds = ReadPositiveWholeNumber(
            configuration,
            "Operations:CompletionCheckSeconds",
            "seconds",
            OperationsOptions.DefaultCompletionCheckSeconds);

        return new OperationsOptions(staleAfterMinutes, completionCheckSeconds);
    }

    /// <summary>
    /// The configured value of <paramref name="key"/>, or <paramref name="defaultValue"/> when it is
    /// absent. Anything that is not a positive whole number is a start-up error.
    /// </summary>
    private static int ReadPositiveWholeNumber(
        IConfiguration configuration,
        string key,
        string unit,
        int defaultValue)
    {
        var configured = configuration[key];
        if (string.IsNullOrWhiteSpace(configured))
        {
            return defaultValue;
        }

        if (!int.TryParse(configured, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            || value < 1)
        {
            throw new InvalidOperationException(
                $"{key} must be a whole number of {unit}, at least 1.");
        }

        return value;
    }
}
