using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartRail.Catering.Application.Webhooks;
using SmartRail.Catering.Application.Webhooks.DispatchDeliveries;

namespace SmartRail.Catering.Infrastructure.Jobs;

/// <summary>
/// Polls for due webhook deliveries (design D2). The job only sends the command; the dispatcher's
/// work runs in that command's handler through the pump. A pass that fails is logged and the next
/// tick retries, so the service cannot die.
/// </summary>
internal sealed class WebhookDispatcherJob(
    IServiceScopeFactory scopeFactory,
    WebhookDispatcherOptions options,
    ILogger<WebhookDispatcherJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.PollIntervalSeconds));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var sender = scope.ServiceProvider.GetRequiredService<ISender>();

                var result = await sender.Send(new DispatchWebhookDeliveriesCommand(), stoppingToken);

                if (result.IsSuccess && result.Value > 0)
                {
                    logger.LogInformation(
                        "Webhook dispatcher attempted {DeliveryCount} delivery(s)",
                        result.Value);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // The host is stopping; pending deliveries are sent after the next start-up.
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Webhook dispatcher pass failed; the next poll will retry");
            }
        }
    }
}
