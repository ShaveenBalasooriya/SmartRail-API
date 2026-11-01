using SmartRail.SharedKernel.Application.Events;

namespace SmartRail.LiveOperations.UnitTests.TestDoubles;

/// <summary>Records outbox publication requests without delivering them.</summary>
internal sealed class SpyIntegrationEventPublisher : IIntegrationEventPublisher
{
    public List<Publication> Publications { get; } = [];

    public Exception? ExceptionToThrow { get; set; }

    public void Publish(object integrationEvent, Guid eventId, string? orderingKey = null)
    {
        Publications.Add(new Publication(integrationEvent, eventId, orderingKey));
        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }
    }

    internal sealed record Publication(object IntegrationEvent, Guid EventId, string? OrderingKey);
}
