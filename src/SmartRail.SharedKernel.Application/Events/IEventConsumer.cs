namespace SmartRail.SharedKernel.Application.Events;

/// <summary>
/// A module's reaction to another module's event (design D4). The dispatcher invokes exactly the
/// registered consumers of an event's type, and delivery is at least once, so a consumer must
/// produce the same effect however many times it receives the same event ID.
/// </summary>
public interface IEventConsumer<TEvent>
{
    /// <summary>
    /// The stable consumer name, for example <c>live-operations.journey-generated</c>. It is stored
    /// with every delivery, so changing it re-delivers the consumer's history; it is also the durable
    /// consumer name when a broker transport replaces the in-process one.
    /// </summary>
    string Name { get; }

    /// <summary>Handles one delivery of <paramref name="integrationEvent"/>.</summary>
    Task Handle(TEvent integrationEvent, CancellationToken cancellationToken);
}
