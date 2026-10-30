using Microsoft.EntityFrameworkCore;
using SmartRail.LiveOperations.Application.Changes;
using SmartRail.LiveOperations.Application.ControllerView;
using SmartRail.LiveOperations.Infrastructure.Persistence;

namespace SmartRail.LiveOperations.Infrastructure.Repositories;

/// <summary>
/// Reads the changes published on a journey from <c>operations.status_changes</c>, in the order
/// they were committed (controller live view, NC-01, NC-06). A change is corrected when another row
/// corrects it; the original row is never rewritten (design D3).
/// </summary>
internal sealed class StatusChangeReader(LiveOperationsDbContext database) : IStatusChangeReader
{
    public async Task<IReadOnlyList<StatusChangeView>> GetForJourneyAsync(
        Guid journeyId,
        CancellationToken cancellationToken = default)
    {
        var changes = await database.StatusChanges
            .AsNoTracking()
            .Where(change => change.JourneyId == journeyId)
            .OrderBy(change => change.FactOrder)
            .Select(change => new
            {
                Change = change,
                Corrected = database.StatusChanges.Any(
                    correction => correction.CorrectsChangeId == change.Id),
            })
            .ToListAsync(cancellationToken);

        return
        [
            .. changes.Select(row => StatusChangeMapping.ToView(row.Change, row.Corrected)),
        ];
    }
}
