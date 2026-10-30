using SmartRail.RailNetwork.Contracts;
using SmartRail.SharedKernel.Application.Messaging;
using SmartRail.SharedKernel.Results;

namespace SmartRail.Catering.Application.Provider.Stations.GetProviderStations;

/// <summary>
/// Every station with its platforms and lines (provider-data-api spec, "Stations and platforms").
/// The station list is small and changes with the seeded network, so it is read whole.
/// </summary>
public sealed record GetProviderStationsQuery : IQuery<IReadOnlyList<ProviderStationResponse>>;

internal sealed class GetProviderStationsQueryHandler(INetworkCatalog network)
    : IQueryHandler<GetProviderStationsQuery, IReadOnlyList<ProviderStationResponse>>
{
    public async Task<Result<IReadOnlyList<ProviderStationResponse>>> Handle(
        GetProviderStationsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var stations = await network.GetStationsAsync(cancellationToken);

        return Result.Success<IReadOnlyList<ProviderStationResponse>>(
            [.. stations.Select(station => station.ToResponse())]);
    }
}
