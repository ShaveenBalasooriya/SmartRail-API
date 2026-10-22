using SmartRail.RailNetwork.Contracts;
using SmartRail.SharedKernel.Application.Messaging;
using SmartRail.SharedKernel.Results;

namespace SmartRail.RailNetwork.Application.Network.GetStation;

internal sealed class GetStationQueryHandler(INetworkRepository network)
    : IQueryHandler<GetStationQuery, StationDto>
{
    private static readonly Error StationNotFound =
        Error.NotFound("station.not_found", "No station matches this identifier.");

    public async Task<Result<StationDto>> Handle(
        GetStationQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var station = await network.GetStationAsync(request.StationId, cancellationToken);

        return station is null
            ? Result.Failure<StationDto>(StationNotFound)
            : Result.Success(station);
    }
}
