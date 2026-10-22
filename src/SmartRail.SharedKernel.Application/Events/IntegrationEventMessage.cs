namespace SmartRail.SharedKernel.Application.Events;

/// <summary>
/// One recorded integration event as the transport sees it (design D2): the outbox row's identity,
/// type, ordering key, payload and occurrence time. It is not the outbox row itself, so the transport
/// and these abstractions stay free of Entity Framework.
/// </summary>
public sealed record IntegrationEventMessage(
    Guid Id,
    string EventType,
    string? OrderingKey,
    string Payload,
    DateTimeOffset OccurredAt);
