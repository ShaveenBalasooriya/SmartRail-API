using SmartRail.SharedKernel.Application.Events;

namespace SmartRail.RailNetwork.UnitTests.TestDoubles;

/// <summary>
/// Stands in for the outbox publisher. The real implementation adds an outbox row in the caller's
/// transaction and nothing reaches a consumer before that transaction commits, so this spy keeps
/// each announced event pending and only counts it as published when the fake unit of work commits.
/// A rollback discards what was pending, exactly as the database would.
/// </summary>
internal sealed class SpyJourneyEventPublisher : IIntegrationEventPublisher
{
    private readonly List<Publication> _pending = [];

    /// <summary>Every event the handler announced, whether or not the transaction later committed.</summary>
    internal List<Publication> Attempted { get; } = [];

    /// <summary>The events that survived the commit and could be delivered.</summary>
    internal List<Publication> Published { get; } = [];

    public void Publish(object integrationEvent, Guid eventId, string? orderingKey = null)
    {
        var publication = new Publication(integrationEvent, eventId, orderingKey);
        Attempted.Add(publication);
        _pending.Add(publication);
    }

    internal void MarkCommitted()
    {
        Published.AddRange(_pending);
        _pending.Clear();
    }

    internal void DiscardPending() => _pending.Clear();

    internal sealed record Publication(object IntegrationEvent, Guid EventId, string? OrderingKey);
}
