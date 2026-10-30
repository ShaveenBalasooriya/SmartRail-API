using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartRail.Distribution.Application;
using SmartRail.Distribution.Application.Abstractions;
using SmartRail.Distribution.Application.Public;
using SmartRail.Distribution.Infrastructure.Public;
using SmartRail.Distribution.Infrastructure.Streams;
using SmartRail.LiveOperations.Contracts;
using SmartRail.SharedKernel.Infrastructure;

namespace SmartRail.Distribution.Infrastructure;

/// <summary>
/// The Distribution module's Infrastructure registration point. The Host calls this and never
/// reaches into the module's internals itself.
/// </summary>
/// <remarks>
/// Distribution keeps no data, so it has no Domain project and no <c>DbContext</c> (design D1).
/// </remarks>
public static class DependencyInjection
{
    /// <summary>The railway's time zone when <c>Railway:TimeZone</c> is not configured; the other modules use the same default.</summary>
    private const string DefaultTimeZoneId = "Asia/Colombo";

    /// <summary>Registers the module's services.</summary>
    public static IServiceCollection AddDistributionInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddDistributionApplication();

        services.AddSingleton(ReadDistributionOptions(configuration));

        // Guidance messages write their times on the railway's clock, in the same configured zone
        // the Rail Network and Live Operations use.
        var configuredTimeZoneId = configuration["Railway:TimeZone"];
        services.AddSingleton(TimeZoneInfo.FindSystemTimeZoneById(
            string.IsNullOrWhiteSpace(configuredTimeZoneId) ? DefaultTimeZoneId : configuredTimeZoneId));

        // The catalogue is read through Rail Network's contracts, once per scope (design D1).
        services.AddScoped<INetworkLookupProvider, NetworkLookupProvider>();

        // Every committed journey state change becomes a stream event for the connections that
        // match it; a redelivery is ignored by the hub's recent-event memory (design D3).
        services.AddEventConsumer<JourneyStateChanged, StreamHubConsumer>();

        return services;
    }

    /// <summary>
    /// Reads <c>Distribution:RefreshAfterSeconds</c> and <c>Distribution:StreamHeartbeatSeconds</c>,
    /// defaulting to fifteen seconds each. A value that is not a positive whole number stops
    /// start-up rather than quietly changing how often a display is told to refresh, or leaving a
    /// stream quieter than a client can tolerate.
    /// </summary>
    private static DistributionOptions ReadDistributionOptions(IConfiguration configuration)
    {
        var refreshAfterSeconds = ReadSeconds(
            configuration,
            "Distribution:RefreshAfterSeconds",
            DistributionOptions.DefaultRefreshAfterSeconds,
            maximum: null);

        var streamHeartbeatSeconds = ReadSeconds(
            configuration,
            "Distribution:StreamHeartbeatSeconds",
            DistributionOptions.DefaultStreamHeartbeatSeconds,
            DistributionOptions.MaximumStreamHeartbeatSeconds);

        return new DistributionOptions(refreshAfterSeconds, streamHeartbeatSeconds);
    }

    /// <summary>Reads one configured interval, or its default when it is not configured.</summary>
    private static int ReadSeconds(
        IConfiguration configuration,
        string setting,
        int defaultValue,
        int? maximum)
    {
        var configured = configuration[setting];

        if (string.IsNullOrWhiteSpace(configured))
        {
            return defaultValue;
        }

        if (!int.TryParse(configured, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            || value < 1
            || (maximum is { } limit && value > limit))
        {
            throw new InvalidOperationException(
                maximum is { } upper
                    ? $"{setting} must be a whole number of seconds, at least 1 and at most {upper}."
                    : $"{setting} must be a whole number of seconds, at least 1.");
        }

        return value;
    }
}
