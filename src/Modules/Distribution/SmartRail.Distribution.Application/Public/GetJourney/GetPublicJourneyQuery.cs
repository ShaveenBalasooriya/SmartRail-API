using SmartRail.Distribution.Application.Abstractions;
using SmartRail.SharedKernel.Application.Messaging;
using SmartRail.SharedKernel.Results;

namespace SmartRail.Distribution.Application.Public.GetJourney;

/// <summary>
/// One journey with every stop in order, the last station it passed, the station it calls at next,
/// how early or late it is, its status, its guidance and its freshness (passenger-departures spec,
/// "Journey detail"). An empty identifier is a validation failure; an unknown journey is a
/// not-found result.
/// </summary>
public sealed record GetPublicJourneyQuery(Guid JourneyId) : IQuery<PublicEnvelope<PublicJourneyView>>;

internal sealed class GetPublicJourneyQueryHandler(
    IPublicJourneyViews views,
    TimeProvider timeProvider,
    DistributionOptions options)
    : IQueryHandler<GetPublicJourneyQuery, PublicEnvelope<PublicJourneyView>>
{
    private static readonly Error UnknownJourney =
        Error.NotFound("journey.not_found", "No journey matches this identifier.");

    public async Task<Result<PublicEnvelope<PublicJourneyView>>> Handle(
        GetPublicJourneyQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // The one view builder produces this answer too, so the detail and the departures list
        // carry the same times, platform, provenance and freshness by construction (design D1).
        var journey = await views.GetAsync(query.JourneyId, cancellationToken);

        if (journey is null)
        {
            return Result.Failure<PublicEnvelope<PublicJourneyView>>(UnknownJourney);
        }

        return Result.Success(new PublicEnvelope<PublicJourneyView>(
            timeProvider.GetUtcNow(),
            options.RefreshAfterSeconds,
            journey));
    }
}
