using System.Text.Json;
using SmartRail.SharedKernel.Application.Events;

namespace SmartRail.SharedKernel.Infrastructure.Events;

/// <summary>
/// The consumers registered for each event type (design D3, D4). A registration records the event
/// type's simple name, which is the outbox row's <c>event_type</c>, and how to resolve that one
/// consumer from a scope, so the transport finds consumers without knowing any module.
/// </summary>
internal sealed class EventConsumerRegistry(IEnumerable<EventConsumerRegistration> registrations)
{
    private readonly ILookup<string, EventConsumerRegistration> byEventType =
        registrations.ToLookup(registration => registration.EventTypeName);

    /// <summary>Whether any consumer is registered for <paramref name="eventType"/>.</summary>
    public bool HasConsumers(string eventType) => byEventType.Contains(eventType);

    /// <summary>The consumers of <paramref name="eventType"/>, resolved from <paramref name="provider"/>.</summary>
    public IReadOnlyList<EventConsumerHandle> Resolve(string eventType, IServiceProvider provider) =>
        [.. byEventType[eventType].Select(registration => registration.Create(provider))];
}

/// <summary>One registered consumer of one event type.</summary>
internal sealed class EventConsumerRegistration(
    string eventTypeName,
    Func<IServiceProvider, EventConsumerHandle> create)
{
    public string EventTypeName { get; } = eventTypeName;

    public EventConsumerHandle Create(IServiceProvider provider) => create(provider);
}

/// <summary>
/// One consumer of one event, with its payload still as the outbox stores it, so the transport
/// neither knows the event's type nor asks a consumer to share its JSON settings.
/// </summary>
internal abstract class EventConsumerHandle
{
    public abstract string Name { get; }

    public abstract Task Handle(string payload, CancellationToken cancellationToken);
}

/// <summary>
/// The typed half of <see cref="EventConsumerHandle"/>: reads the payload with the same
/// System.Text.Json defaults the publisher writes it with (design D2).
/// </summary>
internal sealed class EventConsumerHandle<TEvent>(IEventConsumer<TEvent> consumer) : EventConsumerHandle
{
    public override string Name => consumer.Name;

    public override Task Handle(string payload, CancellationToken cancellationToken) =>
        consumer.Handle(JsonSerializer.Deserialize<TEvent>(payload)!, cancellationToken);
}
