using SmartRail.RailNetwork.Contracts;
using SmartRail.SharedKernel.Application.Messaging;
using SmartRail.SharedKernel.Results;

namespace SmartRail.RailNetwork.Application.Network.GetLines;

internal sealed class GetLinesQueryHandler(INetworkRepository network)
    : IQueryHandler<GetLinesQuery, IReadOnlyList<LineDto>>
{
    public async Task<Result<IReadOnlyList<LineDto>>> Handle(
        GetLinesQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var direction = string.Equals(
            request.Direction,
            nameof(ServiceDirection.Inbound),
            StringComparison.OrdinalIgnoreCase)
            ? ServiceDirection.Inbound
            : ServiceDirection.Outbound;

        return Result.Success(await network.GetLinesAsync(direction, cancellationToken));
    }
}
