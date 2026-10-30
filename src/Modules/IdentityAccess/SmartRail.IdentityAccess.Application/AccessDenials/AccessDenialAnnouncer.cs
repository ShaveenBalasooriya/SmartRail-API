using Microsoft.Extensions.DependencyInjection;
using SmartRail.IdentityAccess.Application.Abstractions;
using SmartRail.IdentityAccess.Contracts;
using SmartRail.SharedKernel.Application.Abstractions;
using SmartRail.SharedKernel.Application.Events;

namespace SmartRail.IdentityAccess.Application.AccessDenials;

/// <summary>
/// Implements <see cref="IAccessDenials"/> by publishing the same <see cref="AccessDenied"/> event the
/// authorisation result handler publishes, and saving it through the module's unit of work (design
/// D6, D9). Inside a state-changing request that save joins the shared request transaction, so the
/// denial commits with the 403 the handler answers; outside one it saves on its own, exactly as the
/// authorisation handler does.
/// </summary>
internal sealed class AccessDenialAnnouncer(
    [FromKeyedServices(IntegrationEventPublisherKeys.Identity)] IIntegrationEventPublisher publisher,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IAccessDenials
{
    public async Task AnnounceAsync(string action, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);

        // One identifier for the payload and the outbox row, so the audit consumer can recognise a
        // redelivery even though the bus hands it only the payload (audit design D5).
        var eventId = Guid.CreateVersion7();

        publisher.Publish(
            new AccessDenied(
                eventId,
                currentUser.StaffId,
                currentUser.Role,
                action,
                timeProvider.GetUtcNow()),
            eventId);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
