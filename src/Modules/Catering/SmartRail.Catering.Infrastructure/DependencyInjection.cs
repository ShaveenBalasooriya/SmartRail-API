using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartRail.Catering.Application;
using SmartRail.Catering.Application.Webhooks;
using SmartRail.Catering.Contracts;
using SmartRail.Catering.Infrastructure.Jobs;
using SmartRail.Catering.Infrastructure.Persistence;
using SmartRail.Catering.Infrastructure.Providers;
using SmartRail.Catering.Infrastructure.Seeding;
using SmartRail.Catering.Infrastructure.Webhooks;
using SmartRail.LiveOperations.Contracts;
using SmartRail.SharedKernel.Application.Abstractions;
using SmartRail.SharedKernel.Application.Events;
using SmartRail.SharedKernel.Infrastructure;
using SmartRail.SharedKernel.Infrastructure.Persistence;

namespace SmartRail.Catering.Infrastructure;

/// <summary>
/// The Catering module's Infrastructure registration point. The Host calls this and never reaches
/// into the module's internals itself.
/// </summary>
public static class DependencyInjection
{
    /// <summary>The railway's time zone when <c>Railway:TimeZone</c> is not configured; the other modules use the same default.</summary>
    private const string DefaultTimeZoneId = "Asia/Colombo";

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

        // The railway's calendar day, the one an absent date means, is read in the same configured
        // zone the other modules use (provider-data-api spec, "Journeys calling at a station").
        var configuredTimeZoneId = configuration["Railway:TimeZone"];
        services.AddSingleton(TimeZoneInfo.FindSystemTimeZoneById(
            string.IsNullOrWhiteSpace(configuredTimeZoneId) ? DefaultTimeZoneId : configuredTimeZoneId));

        services.AddDbContext<CateringDbContext>((provider, options) => options
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsAssembly(typeof(CateringDbContext).Assembly.FullName)
                .MigrationsHistoryTable("__ef_migrations_history", CateringDbContext.Schema))
            .AddRequestTransactionEnlistment(provider));

        // Identity asks this before serving a provider key; the answer reads Catering's own table.
        services.AddScoped<ICateringProviderDirectory, CateringProviderDirectory>();

        // The Host runs every IModuleSeeder after migrations; this module owns only its own schema.
        services.AddScoped<IModuleSeeder, CateringProviderSeeder>();

        // Webhook subscription management (webhook-subscriptions spec).
        services.AddScoped<IWebhookSubscriptionRepository, WebhookSubscriptionRepository>();

        // Delivery records are created by the consumer of the event bus and sent by the dispatcher
        // (webhook-delivery spec; design D1, D2).
        services.AddScoped<IWebhookDeliveryRepository, WebhookDeliveryRepository>();
        services.AddScoped<WebhookPayloadFactory>();
        services.AddEventConsumer<JourneyStateChanged, WebhookDeliveriesConsumer>();

        var webhookOptions = ReadWebhookOptions(configuration);
        services.AddSingleton(webhookOptions);
        services.AddSingleton<IWebhookSender, WebhookSender>();
        services.AddScoped<IWebhookDeliveryPump, WebhookDeliveryPump>();
        services.AddHostedService<WebhookDispatcherJob>();

        return services;
    }

    /// <summary>
    /// Reads the <c>Catering:Webhook</c> section. A value that is not a positive whole number stops
    /// start-up rather than quietly changing the poll interval, the timeout or the retry schedule.
    /// </summary>
    private static WebhookDispatcherOptions ReadWebhookOptions(IConfiguration configuration)
    {
        var pollIntervalSeconds = ReadPositiveWholeNumber(
            configuration,
            "Catering:Webhook:PollIntervalSeconds",
            WebhookDispatcherOptions.DefaultPollIntervalSeconds);

        var requestTimeoutSeconds = ReadPositiveWholeNumber(
            configuration,
            "Catering:Webhook:RequestTimeoutSeconds",
            WebhookDispatcherOptions.DefaultRequestTimeoutSeconds);

        var batchSize = ReadPositiveWholeNumber(
            configuration,
            "Catering:Webhook:BatchSize",
            WebhookDispatcherOptions.DefaultBatchSize);

        var retryDelays = ReadRetryDelays(configuration);

        return new WebhookDispatcherOptions(
            pollIntervalSeconds,
            requestTimeoutSeconds,
            batchSize,
            retryDelays);
    }

    /// <summary>
    /// The configured <c>Catering:Webhook:RetryDelaysSeconds</c> list, or the spec's table. At least
    /// one delay is required; a delivery is abandoned after five failures whatever the table holds.
    /// </summary>
    private static IReadOnlyList<int> ReadRetryDelays(IConfiguration configuration)
    {
        var configured = configuration["Catering:Webhook:RetryDelaysSeconds"];

        if (string.IsNullOrWhiteSpace(configured))
        {
            return WebhookDispatcherOptions.DefaultRetryDelaysSeconds;
        }

        var delays = new List<int>();

        foreach (var part in configured.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
                || seconds < 1)
            {
                throw new InvalidOperationException(
                    "Catering:Webhook:RetryDelaysSeconds must be a comma-separated list of whole seconds, at least 1.");
            }

            delays.Add(seconds);
        }

        if (delays.Count == 0)
        {
            throw new InvalidOperationException(
                "Catering:Webhook:RetryDelaysSeconds must name at least one delay in seconds.");
        }

        return delays;
    }

    /// <summary>
    /// The configured value of <paramref name="key"/>, or <paramref name="defaultValue"/> when it is
    /// absent. Anything that is not a positive whole number is a start-up error.
    /// </summary>
    private static int ReadPositiveWholeNumber(
        IConfiguration configuration,
        string key,
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
            throw new InvalidOperationException($"{key} must be a whole number, at least 1.");
        }

        return value;
    }
}
