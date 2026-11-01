using SmartRail.SharedKernel.Application.Events;

namespace SmartRail.IdentityAccess.UnitTests.TestDoubles;

/// <summary>Records the event and identity supplied to the outbox publisher.</summary>
internal sealed class SpyIntegrationEventPublisher : IIntegrationEventPublisher
{
    public List<(object IntegrationEvent, Guid EventId, string? OrderingKey)> Publications { get; } = [];

    public void Publish(object integrationEvent, Guid eventId, string? orderingKey = null) =>
        Publications.Add((integrationEvent, eventId, orderingKey));
}
