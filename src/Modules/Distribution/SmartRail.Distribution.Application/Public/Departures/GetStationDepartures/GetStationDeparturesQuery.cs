using SmartRail.Distribution.Application.Abstractions;
using SmartRail.SharedKernel.Application.Messaging;
using SmartRail.SharedKernel.Results;

namespace SmartRail.Distribution.Application.Public.Departures.GetStationDepartures;

/// <summary>
/// Upcoming journeys departing one station inside the next window, defaulting to two hours
/// (passenger-departures spec, "Departures from a station"). A window that is not between one
/// minute and six hours is a validation failure; an unknown station is a not-found result.
/// </summary>
public sealed record GetStationDeparturesQuery(Guid StationId, int? WindowMinutes)
    : IQuery<PublicEnvelope<IReadOnlyList<PublicDepartureItem>>>;

internal sealed class GetStationDeparturesQueryHandler(
    IPublicJourneyViews views,
    INetworkLookupProvider lookups,
    TimeProvider timeProvider,
    TimeZoneInfo timeZone,
    DistributionOptions options)
    : IQueryHandler<GetStationDeparturesQuery, PublicEnvelope<IReadOnlyList<PublicDepartureItem>>>
{
    private static readonly Error UnknownStation =
        Error.NotFound("station.not_found", "No station matches this identifier.");

    public async Task<Result<PublicEnvelope<IReadOnlyList<PublicDepartureItem>>>> Handle(
        GetStationDeparturesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var lookup = await lookups.GetAsync([], cancellationToken);

        if (!lookup.Stations.ContainsKey(query.StationId))
        {
            return Result.Failure<PublicEnvelope<IReadOnlyList<PublicDepartureItem>>>(UnknownStation);
        }

        var now = timeProvider.GetUtcNow();
        var journeys = await views.GetByDateAsync(RailwayDate(now), cancellationToken);

        return Result.Success(new PublicEnvelope<IReadOnlyList<PublicDepartureItem>>(
            now,
            options.RefreshAfterSeconds,
            PublicDepartureSelection.FromStation(
                journeys,
                query.StationId,
                query.WindowMinutes ?? PublicDepartureSelection.DefaultWindowMinutes,
                now)));
    }

    /// <summary>Today on the railway's calendar, which is the day the departures are read from.</summary>
    private DateOnly RailwayDate(DateTimeOffset now) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, timeZone).DateTime);
}
