using Microsoft.EntityFrameworkCore;
using SmartRail.LiveOperations.Application.LiveJourneys;
using SmartRail.LiveOperations.Domain.LiveState;
using SmartRail.LiveOperations.Infrastructure.Persistence;

namespace SmartRail.LiveOperations.Infrastructure.Repositories;

/// <summary>
/// Reads journeys and their stops from the module's own schema for the live-journey queries
/// (design D4). Everything is untracked, so no entity is ever changed by a read.
/// </summary>
internal sealed class LiveJourneyRepository(LiveOperationsDbContext database) : ILiveJourneyRepository
{
    public Task<JourneyLiveState?> GetAsync(
        Guid journeyId,
        CancellationToken cancellationToken = default)
    {
        return database.JourneyLiveStates
            .AsNoTracking()
            .Include(state => state.Stops)
            .SingleOrDefaultAsync(state => state.Id == journeyId, cancellationToken);
    }

    public async Task<IReadOnlyList<JourneyLiveState>> GetByDateAsync(
        DateOnly serviceDate,
        CancellationToken cancellationToken = default)
    {
        var states = await database.JourneyLiveStates
            .AsNoTracking()
            .Include(state => state.Stops)
            .Where(state => state.ServiceDate == serviceDate)
            .ToListAsync(cancellationToken);

        // Ordered by scheduled departure, then by identifier so the order is stable. Ordering in
        // memory keeps the rule the same as the contract's documented "scheduled departure order".
        return
        [
            .. states
                .OrderBy(state => state.Stops
                    .Where(stop => stop.ScheduledDeparture != null)
                    .OrderBy(stop => stop.Sequence)
                    .Select(stop => stop.ScheduledDeparture)
                    .FirstOrDefault())
                .ThenBy(state => state.Id),
        ];
    }
}
