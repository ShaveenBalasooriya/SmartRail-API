using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartRail.SharedKernel.Application.Events;

namespace SmartRail.SharedKernel.Infrastructure.Persistence;

/// <summary>
/// The publisher a module is registered for (design D1, D2). It writes the outbox row through the
/// module's own <see cref="DbContext"/>, so the caller's unit of work saves it with the change. It
/// never saves itself, so a rollback publishes nothing.
/// </summary>
internal sealed class OutboxIntegrationEventPublisher(
    DbContext dbContext,
    TimeProvider timeProvider) : IIntegrationEventPublisher
{
    public void Publish(object integrationEvent, Guid eventId, string? orderingKey = null)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var message = new OutboxMessage(
            eventId,
            integrationEvent.GetType().Name,
            orderingKey,
            JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType()),
            timeProvider.GetUtcNow());

        dbContext.Add(message);
    }
}
