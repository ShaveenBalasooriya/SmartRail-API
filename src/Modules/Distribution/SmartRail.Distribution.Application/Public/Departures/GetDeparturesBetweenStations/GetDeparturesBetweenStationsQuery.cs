using SmartRail.Distribution.Application.Abstractions;
using SmartRail.SharedKernel.Application.Messaging;
using SmartRail.SharedKernel.Results;

namespace SmartRail.Distribution.Application.Public.Departures.GetDeparturesBetweenStations;

/// <summary>
/// Upcoming journeys that call at one station and later at another today, with the times, platform,
/// status and freshness a passenger needs (passenger-departures spec, "Departures between two
/// stations"). An empty origin or destination is a validation failure; an unknown station is a
/// not-found result. Stations on different lines simply return no journeys.
/// </summary>
public sealed record GetDeparturesBetweenStationsQuery(Guid From, Guid To)
    : IQuery<PublicEnvelope<IReadOnlyList<PublicDepartureItem>>>;

internal sealed class GetDeparturesBetweenStationsQueryHandler(
    IPublicJourneyViews views,
    INetworkLookupProvider lookups,
    TimeProvider timeProvider,
    TimeZoneInfo timeZone,
    DistributionOptions options)
    : IQueryHandler<GetDeparturesBetweenStationsQuery, PublicEnvelope<IReadOnlyList<PublicDepartureItem>>>
{
    private static readonly Error UnknownStation =
        Error.NotFound("station.not_found", "No station matches this identifier.");

    public async Task<Result<PublicEnvelope<IReadOnlyList<PublicDepartureItem>>>> Handle(
        GetDeparturesBetweenStationsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // The catalogue answers whether the pair exists at all, before the day is read. It is read
        // once per request, so the public view reuses the same copy.
        var lookup = await lookups.GetAsync([], cancellationToken);

        if (!lookup.Stations.ContainsKey(query.From) || !lookup.Stations.ContainsKey(query.To))
        {
            return Result.Failure<PublicEnvelope<IReadOnlyList<PublicDepartureItem>>>(UnknownStation);
        }

        var now = timeProvider.GetUtcNow();
        var journeys = await views.GetByDateAsync(RailwayDate(now), cancellationToken);

        return Result.Success(new PublicEnvelope<IReadOnlyList<PublicDepartureItem>>(
            now,
            options.RefreshAfterSeconds,
            PublicDepartureSelection.Between(journeys, query.From, query.To, now)));
    }

    /// <summary>Today on the railway's calendar, which is the day the departures are read from.</summary>
    private DateOnly RailwayDate(DateTimeOffset now) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, timeZone).DateTime);
}
