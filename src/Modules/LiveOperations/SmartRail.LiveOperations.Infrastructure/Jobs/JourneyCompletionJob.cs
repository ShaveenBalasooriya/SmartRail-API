using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartRail.LiveOperations.Application.Abstractions;
using SmartRail.LiveOperations.Application.LiveState.CompleteFinishedJourneys;

namespace SmartRail.LiveOperations.Infrastructure.Jobs;

/// <summary>
/// Completes journeys whose terminus arrival has passed (design D5). A journey finishes without
/// anyone acting when the clock passes it, and the passing of time is not a recorded fact, so this
/// check is what asks the recompute to run. It fires only after the configured interval, never at
/// start-up, and a run that fails is logged and tried again on the next tick, so the service cannot
/// die. The job only sends the command; the work is in its handler.
/// </summary>
internal sealed class JourneyCompletionJob(
    IServiceScopeFactory scopeFactory,
    OperationsOptions options,
    TimeProvider timeProvider,
    ILogger<JourneyCompletionJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(options.CompletionCheckSeconds),
            timeProvider);

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

            var result = await sender.Send(new CompleteFinishedJourneysCommand(), cancellationToken);

            if (result.IsSuccess && result.Value > 0)
            {
                logger.LogInformation(
                    "Journey completion check processed {JourneyCount} running journey(s)",
                    result.Value);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The host is stopping; the next start replaces the missed check.
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Journey completion check failed; the next run will retry");
        }
    }
}
