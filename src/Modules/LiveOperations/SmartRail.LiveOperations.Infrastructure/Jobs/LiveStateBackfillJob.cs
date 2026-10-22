using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartRail.LiveOperations.Application.LiveState.BackfillLiveState;

namespace SmartRail.LiveOperations.Infrastructure.Jobs;

/// <summary>
/// Repairs journeys whose announcement was lost: journey generation inserts the row and then
/// announces it, and the two are not one transaction (design D4). It runs once at start-up, after
/// generation has had its chance, and then every hour; every run is idempotent. The job only sends
/// the command; the work is in its handler.
/// </summary>
internal sealed class LiveStateBackfillJob(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<LiveStateBackfillJob> logger) : BackgroundService
{
    /// <summary>
    /// Journey generation is registered before this service and its first run is a few queries, so
    /// a short wait lets its announcements land before the backfill looks for journeys missing state.
    /// </summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan RunInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, timeProvider, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

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
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();

            var result = await sender.Send(new BackfillLiveStateCommand(), cancellationToken);

            if (result.IsSuccess && result.Value > 0)
            {
                logger.LogInformation(
                    "Live state backfill created state for {JourneyCount} journey(s)",
                    result.Value);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The host is stopping; the next start-up run repairs anything left over.
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Live state backfill run failed; the next run will retry");
        }
    }
}
