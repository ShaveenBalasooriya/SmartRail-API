using Microsoft.EntityFrameworkCore.Diagnostics;

namespace SmartRail.SharedKernel.Infrastructure.Events;

/// <summary>
/// Sets the dispatcher's signal after an outbox-owning context has saved (design D7). It is attached
/// to exactly those contexts, through <see cref="OutboxSignalExtensions.AddOutboxSignal"/>.
/// </summary>
internal sealed class OutboxSignalInterceptor(OutboxSignal signal) : SaveChangesInterceptor
{
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        signal.Notify();
        return base.SavedChanges(eventData, result);
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        signal.Notify();
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }
}
