using Microsoft.EntityFrameworkCore;
using SmartRail.LiveOperations.Application.JourneyEvents;
using SmartRail.LiveOperations.Domain.Changes;
using SmartRail.LiveOperations.Domain.LiveState;
using SmartRail.LiveOperations.Infrastructure.Persistence;

namespace SmartRail.LiveOperations.Infrastructure.Repositories;

/// <summary>Reads the append-only journey event stream (design D4).</summary>
internal sealed class JourneyEventRepository(LiveOperationsDbContext database) : IJourneyEventRepository
{
    public async Task<IReadOnlyList<JourneyEvent>> GetAfterAsync(
        long position,
        Guid? journeyId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var events = database.JourneyEvents
            .AsNoTracking()
            .Where(journeyEvent => journeyEvent.StreamPosition > position);

        if (journeyId is { } filter)
        {
            events = events.Where(journeyEvent => journeyEvent.JourneyId == filter);
        }

        return await events
            .OrderBy(journeyEvent => journeyEvent.StreamPosition)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public Task<long?> GetOldestPositionAsync(CancellationToken cancellationToken = default)
    {
        return database.JourneyEvents
            .AsNoTracking()
            .MinAsync(journeyEvent => (long?)journeyEvent.StreamPosition, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, CorrectionReference>> GetCorrectionReferencesAsync(
        IReadOnlyCollection<Guid> correctionChangeIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(correctionChangeIds);

        var ids = correctionChangeIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, CorrectionReference>();
        }

        // journey_events (the correction) -> status_changes (what it corrects) -> journey_events
        // (the event the original change appended). No journey event column is added: the link is
        // read from the rows that already record it.
        var rows = await (
                from correction in database.StatusChanges.AsNoTracking()
                where correction.Type == StatusChangeType.Correction
                    && correction.CorrectsChangeId != null
                    && ids.Contains(correction.Id)
                join originalEvent in database.JourneyEvents.AsNoTracking()
                    on correction.CorrectsChangeId equals originalEvent.StatusChangeId into originalEvents
                from originalEvent in originalEvents.DefaultIfEmpty()
                select new
                {
                    CorrectionId = correction.Id,
                    CorrectsChangeId = correction.CorrectsChangeId!.Value,
                    CorrectsEventId = originalEvent == null ? (Guid?)null : originalEvent.Id,
                })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.CorrectionId)
            .ToDictionary(
                group => group.Key,
                group => new CorrectionReference(
                    group.First().CorrectsChangeId,
                    group.Select(row => row.CorrectsEventId).FirstOrDefault(eventId => eventId is not null)));
    }
}
