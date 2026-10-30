using SmartRail.SimulatedProvider.Clients;
using SmartRail.SimulatedProvider.Configuration;
using SmartRail.SimulatedProvider.Processing;

namespace SmartRail.SimulatedProvider.Startup;

/// <summary>
/// Registers this provider's webhook and loads today's journeys on start-up (simulated-provider spec,
/// "Registers and loads state"). It retries until SmartRail answers, because Compose may start this
/// service before the API is ready; once the journeys are loaded the loop ends.
/// </summary>
public sealed class StartupLoader(
    SmartRailProviderClient client,
    JourneyStore store,
    SimulatedProviderOptions options,
    ILogger<StartupLoader> logger) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (await TryLoadAsync(stoppingToken))
            {
                return;
            }

            try
            {
                await Task.Delay(RetryDelay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task<bool> TryLoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var registration = await client.RegisterWebhookAsync(options.CallbackUrl, cancellationToken);

            if (registration.Error is not null)
            {
                store.RecordRegistration(null, options.CallbackUrl, registration.Error);
                logger.LogWarning("Webhook registration failed: {Error}", registration.Error);

                return false;
            }

            store.RecordRegistration(registration.SubscriptionId, options.CallbackUrl, null);

            var station = await client.GetStationAsync(options.StationId, cancellationToken);
            var journeys = await client.GetJourneysAsync(options.StationId, cancellationToken);

            store.LoadJourneys(station, journeys, DateTimeOffset.UtcNow);

            logger.LogInformation(
                "Simulated provider registered subscription {SubscriptionId} and loaded {JourneyCount} journey(s) at {StationId}",
                registration.SubscriptionId,
                journeys.Count,
                options.StationId);

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            store.RecordRegistration(null, options.CallbackUrl, exception.GetType().Name);
            logger.LogWarning(
                "SmartRail is not reachable yet ({ExceptionType}); retrying in {RetrySeconds} s",
                exception.GetType().Name,
                RetryDelay.TotalSeconds);

            return false;
        }
    }
}
