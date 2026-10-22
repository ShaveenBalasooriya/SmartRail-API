using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartRail.SharedKernel.Application.Events;
using SmartRail.SharedKernel.Infrastructure.Persistence;

namespace SmartRail.SharedKernel.Infrastructure.Events;

/// <summary>
/// The single dispatcher (design D3, D7). Each cycle, for every registered outbox, it opens a scope,
/// resolves that module's context and transport, publishes the unpublished rows in <c>position</c>
/// order and then delivers what is due. It has no module knowledge, and tolerates no outbox being
/// registered at all.
/// </summary>
internal sealed class EventDispatcher(
    IServiceScopeFactory scopeFactory,
    IEnumerable<IOutboxSource> sources,
    OutboxSignal signal,
    TimeProvider timeProvider,
    ILogger<EventDispatcher> logger) : BackgroundService
{
    /// <summary>The fallback when a wake-up is missed (design D7).</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var source in sources)
            {
                try
                {
                    await DispatchAsync(source, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    // One module's outbox failing must not stop the others or the host.
                    logger.LogError(
                        exception,
                        "Dispatching the {Schema} outbox failed; the next poll will retry",
                        source.Schema);
                }
            }

            try
            {
                await signal.WaitAsync(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task DispatchAsync(IOutboxSource source, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();

        var context = (DbContext)scope.ServiceProvider.GetRequiredService(source.ContextType);
        var transport = scope.ServiceProvider.GetRequiredKeyedService<IEventTransport>(source.Schema);

        var messages = await context.Set<OutboxMessage>()
            .Where(message => message.PublishedAt == null)
            .OrderBy(message => message.Position)
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            await transport.Publish(
                new IntegrationEventMessage(
                    message.Id,
                    message.EventType,
                    message.OrderingKey,
                    message.Payload,
                    message.OccurredAt),
                cancellationToken);

            // Published at is only set once the transport has accepted the message, so a crash
            // before this point republishes it, which the transport makes harmless (design D3).
            message.PublishedAt = timeProvider.GetUtcNow();
            await context.SaveChangesAsync(cancellationToken);
        }

        await transport.DeliverPending(cancellationToken);
    }
}
