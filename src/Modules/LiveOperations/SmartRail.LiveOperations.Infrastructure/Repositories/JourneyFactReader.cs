using Microsoft.EntityFrameworkCore;
using SmartRail.LiveOperations.Application.LiveState;
using SmartRail.LiveOperations.Domain.Calculation;
using SmartRail.LiveOperations.Domain.Changes;
using SmartRail.LiveOperations.Infrastructure.Persistence;

namespace SmartRail.LiveOperations.Infrastructure.Repositories;

/// <summary>
/// Reads every fact recorded for a journey: the changes the Network Controller published and the
/// departures the Guard logged, ordered by the order the database committed them
/// (<c>fact_order</c>). Both tables read the same database sequence, so a departure and a published
/// change that affect the same stop stay ordered against each other (controller-operations package
/// decision D-6).
/// </summary>
internal sealed class JourneyFactReader(LiveOperationsDbContext database) : IJourneyFactReader
{
    public async Task<IReadOnlyList<Fact>> GetFactsAsync(
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

        var departures = await database.DepartureLogs
            .AsNoTracking()
            .Where(log => log.JourneyId == journeyId)
            .OrderBy(log => log.FactOrder)
            .Select(log => new
            {
                log.StopSequence,
                log.DepartedAt,
                log.ReceivedAt,
                log.FactOrder,
            })
            .ToListAsync(cancellationToken);

        var facts = new List<Fact>(changes.Count + departures.Count);

        foreach (var row in changes)
        {
            var change = row.Change;

            switch (change.Type)
            {
                case StatusChangeType.Delay:
                    facts.Add(new ChangeFact(
                        ChangeKind.Delay,
                        change.StopSequence,
                        change.DelayMinutes,
                        PlatformId: null,
                        change.PublishedAt,
                        change.FactOrder,
                        row.Corrected));
                    break;

                case StatusChangeType.Cancellation:
                    facts.Add(new ChangeFact(
                        ChangeKind.Cancellation,
                        StopSequence: null,
                        DelayMinutes: null,
                        PlatformId: null,
                        change.PublishedAt,
                        change.FactOrder,
                        row.Corrected));
                    break;

                case StatusChangeType.PlatformChange:
                    facts.Add(new ChangeFact(
                        ChangeKind.PlatformChange,
                        change.StopSequence,
                        DelayMinutes: null,
                        change.NewPlatformId,
                        change.PublishedAt,
                        change.FactOrder,
                        row.Corrected));
                    break;

                // A correction withdraws the change it references and contributes no fact of its own.
                case StatusChangeType.Correction:
                    break;

                case StatusChangeType.Termination:
                    facts.Add(new ChangeFact(
                        ChangeKind.Termination,
                        change.StopSequence,
                        DelayMinutes: null,
                        PlatformId: null,
                        change.PublishedAt,
                        change.FactOrder,
                        row.Corrected));
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Unknown status change type '{change.Type}'.");
            }
        }

        // The Guard's departure logs: the device time is the departure, the receipt time only feeds
        // the freshness shown on every channel (GRD-05).
        foreach (var departure in departures)
        {
            facts.Add(new DepartureFact(
                departure.StopSequence,
                departure.DepartedAt,
                departure.ReceivedAt,
                departure.FactOrder));
        }

        return facts.OrderBy(fact => fact.StreamOrder).ToList();
    }
}
