using System.Collections.Concurrent;
using MediatR;
using SmartRail.IdentityAccess.Contracts;

namespace SmartRail.Host.IntegrationTests.Infrastructure;

/// <summary>
/// Captures the access-denied notifications the Host published, so a test can assert that a denied
/// write is announced exactly once and a denied read is not announced at all.
/// </summary>
public sealed class AccessDeniedRecorder : INotificationHandler<AccessDenied>
{
    private readonly ConcurrentQueue<AccessDenied> _denials = new();

    /// <summary>The notifications recorded so far, oldest first.</summary>
    public IReadOnlyCollection<AccessDenied> Denied => _denials;

    public Task Handle(AccessDenied notification, CancellationToken cancellationToken)
    {
        _denials.Enqueue(notification);
        return Task.CompletedTask;
    }
}
