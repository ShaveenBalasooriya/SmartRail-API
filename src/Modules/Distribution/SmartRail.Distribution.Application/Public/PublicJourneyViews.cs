using SmartRail.Distribution.Application.Public.Guidance;
using SmartRail.LiveOperations.Contracts;

namespace SmartRail.Distribution.Application.Public;

/// <summary>
/// The public read of journeys, composed from Live Operations' live state and the Rail Network
/// catalogue. The passenger endpoints, the display feed and the streams all read journeys through
/// it, so one builder produces every item they return (design D1).
/// </summary>
public interface IPublicJourneyViews
{
    /// <summary>The public view of one journey, or <see langword="null"/> when the ID is unknown.</summary>
    Task<PublicJourneyView?> GetAsync(Guid journeyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The public view of every journey on one railway calendar day, in the order Live Operations
    /// returns them.
    /// </summary>
    Task<IReadOnlyList<PublicJourneyView>> GetByDateAsync(
        DateOnly serviceDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The public view of each of the given live states, in the order given. A resuming stream
    /// builds its catch-up payloads from the snapshot a stored event carries (design D4), so a
    /// caught-up event and a live one are the same document.
    /// </summary>
    Task<IReadOnlyList<PublicJourneyView>> BuildAsync(
        IReadOnlyList<LiveJourneyView> journeys,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Composes the live state with the names a public view needs. Both are read through their module's
/// contract, never from another module's schema (architecture §3).
/// </summary>
internal sealed class PublicJourneyViews(
    ILiveJourneys liveJourneys,
    INetworkLookupProvider lookups,
    TimeZoneInfo timeZone) : IPublicJourneyViews
{
    public async Task<PublicJourneyView?> GetAsync(
        Guid journeyId,
        CancellationToken cancellationToken = default)
    {
        var journey = await liveJourneys.GetAsync(journeyId, cancellationToken);

        return journey is null
            ? null
            : (await BuildAsync([journey], wholeDay: false, cancellationToken))[0];
    }

    public async Task<IReadOnlyList<PublicJourneyView>> GetByDateAsync(
        DateOnly serviceDate,
        CancellationToken cancellationToken = default)
    {
        var journeys = await liveJourneys.GetByDateAsync(serviceDate, cancellationToken);

        return await BuildAsync(journeys, wholeDay: true, cancellationToken);
    }

    public Task<IReadOnlyList<PublicJourneyView>> BuildAsync(
        IReadOnlyList<LiveJourneyView> journeys,
        CancellationToken cancellationToken = default) =>
        BuildAsync(journeys, wholeDay: false, cancellationToken);

    /// <summary>
    /// Builds one view per journey and fills its guidance. The next service comes from the same day,
    /// so the day is read once and only when an item's guidance needs one (design D6); a caller that
    /// asked for the whole day has already read it.
    /// </summary>
    private async Task<IReadOnlyList<PublicJourneyView>> BuildAsync(
        IReadOnlyList<LiveJourneyView> journeys,
        bool wholeDay,
        CancellationToken cancellationToken)
    {
        if (journeys.Count == 0)
        {
            return [];
        }

        var sameDayJourneys = wholeDay || !journeys.Any(GuidanceBuilder.NeedsNextService)
            ? journeys
            : await liveJourneys.GetByDateAsync(journeys[0].ServiceDate, cancellationToken);

        var serviceIds = journeys
            .Concat(sameDayJourneys)
            .Select(journey => journey.ServiceId)
            .Distinct()
            .ToArray();

        var lookup = await lookups.GetAsync(serviceIds, cancellationToken);

        return journeys
            .Select(journey => PublicJourneyViewBuilder.Build(journey, lookup) with
            {
                Guidance = GuidanceBuilder.Build(journey, sameDayJourneys, lookup, timeZone),
            })
            .ToArray();
    }
}
