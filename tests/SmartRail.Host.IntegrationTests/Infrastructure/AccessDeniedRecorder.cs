using System.Collections.Concurrent;
using SmartRail.IdentityAccess.Contracts;
using SmartRail.SharedKernel.Application.Events;

namespace SmartRail.Host.IntegrationTests.Infrastructure;

/// <summary>
/// Captures the access-denied events the event bus delivered, so a test can assert that a denied
/// write is announced exactly once and a denied read is not announced at all (design D9).
/// </summary>
public sealed class AccessDeniedRecorder : IEventConsumer<AccessDenied>
{
    private readonly ConcurrentQueue<AccessDenied> _denials = new();

    /// <summary>The stable consumer name the bus stores each delivery under.</summary>
    public string Name => "test.access-denied";

    /// <summary>The events delivered so far, oldest first.</summary>
    public IReadOnlyCollection<AccessDenied> Denied => _denials;

    public Task Handle(AccessDenied integrationEvent, CancellationToken cancellationToken)
    {
        _denials.Enqueue(integrationEvent);
        return Task.CompletedTask;
    }
}
