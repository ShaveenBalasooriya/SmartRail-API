using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartRail.RailNetwork.Application.Journeys.GenerateJourneys;

namespace SmartRail.RailNetwork.Infrastructure.Jobs;

/// <summary>
/// Sends the generation command once at start-up and then every hour (journey-generation spec).
/// A failed run is logged and the next run still happens, so a transient database problem cannot
/// stop the host or stop journeys being generated later. The job only sends the command; the work
/// is in its handler (AGENTS.md §3).
/// </summary>
public sealed class JourneyGenerationJob(
    IServiceScopeFactory scopeFactory,
    ILogger<JourneyGenerationJob> logger) : BackgroundService
{
    private static readonly TimeSpan RunInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunOnceAsync(stoppingToken);

        using var timer = new PeriodicTimer(RunInterval);

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
            await sender.Send(new GenerateJourneysCommand(), cancellationToken);
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
