using Microsoft.EntityFrameworkCore;
using SmartRail.LiveOperations.Application.JourneyEvents;
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
}
