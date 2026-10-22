using SmartRail.RailNetwork.Contracts;
using SmartRail.SharedKernel.Application.Messaging;
using SmartRail.SharedKernel.Results;

namespace SmartRail.RailNetwork.Application.Network.GetStations;

internal sealed class GetStationsQueryHandler(INetworkRepository network)
    : IQueryHandler<GetStationsQuery, IReadOnlyList<StationDto>>
{
    public async Task<Result<IReadOnlyList<StationDto>>> Handle(
        GetStationsQuery request,
        CancellationToken cancellationToken)
    {
        return Result.Success(await network.GetStationsAsync(cancellationToken));
    }
}
