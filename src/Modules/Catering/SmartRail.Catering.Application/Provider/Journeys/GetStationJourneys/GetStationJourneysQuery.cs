using SmartRail.Catering.Application.Provider.Stations;
using SmartRail.LiveOperations.Contracts;
using SmartRail.RailNetwork.Contracts;
using SmartRail.SharedKernel.Application.Messaging;
using SmartRail.SharedKernel.Results;

namespace SmartRail.Catering.Application.Provider.Journeys.GetStationJourneys;

/// <summary>
/// The journeys calling at one station on one railway calendar day, ordered by expected arrival
/// (provider-data-api spec, "Journeys calling at a station"). A missing date means today; only today
/// and tomorrow are accepted, and an unknown station is a not-found result.
/// </summary>
public sealed record GetStationJourneysQuery(Guid StationId, DateOnly? Date)
    : IQuery<IReadOnlyList<ProviderJourneyResponse>>;

internal sealed class GetStationJourneysQueryHandler(
    INetworkCatalog network,
    ITimetable timetable,
    ILiveJourneys liveJourneys,
    TimeProvider timeProvider,
    TimeZoneInfo timeZone)
    : IQueryHandler<GetStationJourneysQuery, IReadOnlyList<ProviderJourneyResponse>>
{
    private static readonly Error UnknownStation =
        Error.NotFound("station.not_found", "No station matches this identifier.");

    private readonly Dictionary<Guid, ServiceDirection?> _directions = [];

    public async Task<Result<IReadOnlyList<ProviderJourneyResponse>>> Handle(
        GetStationJourneysQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var stations = await network.GetStationsAsync(cancellationToken);
        var stationsById = stations.ToDictionary(station => station.Id);

        if (!stationsById.TryGetValue(query.StationId, out var station))
        {
            return Result.Failure<IReadOnlyList<ProviderJourneyResponse>>(UnknownStation);
        }

        var platforms = station.Platforms.ToDictionary(platform => platform.Id);
        var lines = (await network.GetLinesAsync(cancellationToken: cancellationToken))
            .GroupBy(line => line.Id)
            .ToDictionary(group => group.Key, group => group.First());

        var serviceDate = query.Date ?? RailwayDate(timeProvider.GetUtcNow());
        var journeys = await liveJourneys.GetByDateAsync(serviceDate, cancellationToken);

        var items = new List<ProviderJourneyResponse>();

        foreach (var journey in journeys)
        {
            var stop = journey.Stops
                .Where(candidate => candidate.IsCalling && candidate.StationId == query.StationId)
                .OrderBy(candidate => candidate.Sequence)
                .FirstOrDefault();

            // A journey that passes the station without calling, or does not reach it, is not
            // listed (spec, "Journeys calling at a station"). A terminating journey's arrival at
            // its last calling stop is enough to include it.
            if (stop is null)
            {
                continue;
            }

            var destination = journey.Stops
                .Where(candidate => candidate.IsCalling)
                .OrderBy(candidate => candidate.Sequence)
                .Last();

            var direction = await DirectionAsync(journey.ServiceId, cancellationToken);

            items.Add(new ProviderJourneyResponse(
                journey.JourneyId,
                new ProviderLineResponse(journey.LineId, lines.GetValueOrDefault(journey.LineId)?.Name),
                direction,
                new ProviderStationRefResponse(
                    destination.StationId,
                    stationsById.GetValueOrDefault(destination.StationId)?.Name),
                stop.ScheduledArrival,
                stop.ExpectedArrival,
                stop.PlatformId is { } platformId && platforms.TryGetValue(platformId, out var platform)
                    ? new ProviderPlatformResponse(platform.Id, platform.Number)
                    : null,
                journey.Status,
                journey.LastSequenceNumber,
                journey.LastUpdatedAt));
        }

        return Result.Success<IReadOnlyList<ProviderJourneyResponse>>(
            [.. items
                .OrderBy(item => item.ExpectedArrival ?? item.ScheduledArrival ?? DateTimeOffset.MaxValue)
                .ThenBy(item => item.JourneyId)]);
    }

    /// <summary>The service's direction, read once per service; null when the timetable no longer holds it.</summary>
    private async Task<ServiceDirection?> DirectionAsync(Guid serviceId, CancellationToken cancellationToken)
    {
        if (!_directions.TryGetValue(serviceId, out var direction))
        {
            var service = await timetable.GetServiceAsync(serviceId, cancellationToken);
            direction = service?.Direction;
            _directions[serviceId] = direction;
        }

        return direction;
    }

    /// <summary>Today on the railway's calendar, which is the day the journeys are read from.</summary>
    private DateOnly RailwayDate(DateTimeOffset now) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, timeZone).DateTime);
}
