using SmartRail.Distribution.Application.Abstractions;
using SmartRail.SharedKernel.Application.Messaging;
using SmartRail.SharedKernel.Results;

namespace SmartRail.Distribution.Application.Public.Display;

/// <summary>
/// The next departures from one platform of one station, eight by default and at most twenty
/// (station-display-feed spec, "Platform departures feed"). A count outside one to twenty is a
/// validation failure; an unknown station, or a platform number the station does not have, is a
/// not-found result.
/// </summary>
public sealed record GetPlatformFeedQuery(Guid StationId, string PlatformNumber, int? Count)
    : IQuery<PublicEnvelope<PublicPlatformFeed>>;

internal sealed class GetPlatformFeedQueryHandler(
    IPublicJourneyViews views,
    INetworkLookupProvider lookups,
    TimeProvider timeProvider,
    TimeZoneInfo timeZone,
    DistributionOptions options)
    : IQueryHandler<GetPlatformFeedQuery, PublicEnvelope<PublicPlatformFeed>>
{
    private static readonly Error UnknownStation =
        Error.NotFound("station.not_found", "No station matches this identifier.");

    private static readonly Error UnknownPlatform =
        Error.NotFound("platform.not_found", "The station has no platform with that number.");

    public async Task<Result<PublicEnvelope<PublicPlatformFeed>>> Handle(
        GetPlatformFeedQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var lookup = await lookups.GetAsync([], cancellationToken);

        if (!lookup.Stations.TryGetValue(query.StationId, out var station))
        {
            return Result.Failure<PublicEnvelope<PublicPlatformFeed>>(UnknownStation);
        }

        var platform = station.Platforms.FirstOrDefault(candidate =>
            string.Equals(candidate.Number, query.PlatformNumber, StringComparison.Ordinal));

        if (platform is null)
        {
            return Result.Failure<PublicEnvelope<PublicPlatformFeed>>(UnknownPlatform);
        }

        var now = timeProvider.GetUtcNow();
        var journeys = await views.GetByDateAsync(RailwayDate(now), cancellationToken);

        // The timetable's own stops are needed only for the moved notices, which stand on the
        // platform a journey was assigned before its change.
        var services = await lookups.GetAsync(
            [.. journeys.Select(journey => journey.ServiceId).Distinct()],
            cancellationToken);

        return Result.Success(new PublicEnvelope<PublicPlatformFeed>(
            now,
            options.RefreshAfterSeconds,
            PublicPlatformSelection.Feed(
                journeys,
                query.StationId,
                platform.Id,
                query.Count ?? PublicPlatformSelection.DefaultCount,
                now,
                services,
                timeZone)));
    }

    /// <summary>Today on the railway's calendar, which is the day the departures are read from.</summary>
    private DateOnly RailwayDate(DateTimeOffset now) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, timeZone).DateTime);
}
