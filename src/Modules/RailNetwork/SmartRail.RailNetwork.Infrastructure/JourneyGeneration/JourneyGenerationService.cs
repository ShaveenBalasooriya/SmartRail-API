using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SmartRail.RailNetwork.Infrastructure.JourneyGeneration;

/// <summary>
/// Runs journey generation once at start-up and then every hour (design D2). A failed run is
/// logged and the next run still happens, so a transient database problem cannot stop the host
/// or stop journeys from being generated later.
/// </summary>
public sealed class JourneyGenerationService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<JourneyGenerationService> logger) : BackgroundService
{
    private static readonly TimeSpan RunInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunOnceAsync(stoppingToken);

        using var timer = new PeriodicTimer(RunInterval, timeProvider);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunOnceAsync(stoppingToken);
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var generator = scope.ServiceProvider.GetRequiredService<JourneyGenerator>();
            await generator.GenerateAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The host is stopping; the next start-up run repairs anything left over.
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Journey generation run failed; the next run will retry");
        }
    }
}
