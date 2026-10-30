using SmartRail.RailNetwork.Contracts;
using SmartRail.SharedKernel.Application.Messaging;
using SmartRail.SharedKernel.Results;

namespace SmartRail.Catering.Application.Provider.Stations.GetProviderStation;

/// <summary>
/// One station with its platforms and lines (provider-data-api spec, "Stations and platforms"). An
/// empty identifier is a validation failure; an unknown station is a not-found result.
/// </summary>
public sealed record GetProviderStationQuery(Guid StationId) : IQuery<ProviderStationResponse>;

internal sealed class GetProviderStationQueryHandler(INetworkCatalog network)
    : IQueryHandler<GetProviderStationQuery, ProviderStationResponse>
{
    private static readonly Error StationNotFound =
        Error.NotFound("station.not_found", "No station matches this identifier.");

    public async Task<Result<ProviderStationResponse>> Handle(
        GetProviderStationQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var station = await network.GetStationAsync(query.StationId, cancellationToken);

        return station is null
            ? Result.Failure<ProviderStationResponse>(StationNotFound)
            : Result.Success(station.ToResponse());
    }
}
