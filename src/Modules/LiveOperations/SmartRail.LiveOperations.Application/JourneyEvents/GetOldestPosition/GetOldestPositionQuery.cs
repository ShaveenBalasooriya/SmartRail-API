using SmartRail.SharedKernel.Application.Messaging;
using SmartRail.SharedKernel.Results;

namespace SmartRail.LiveOperations.Application.JourneyEvents.GetOldestPosition;

/// <summary>
/// The lowest stream position still retained, or <see langword="null"/> when no event exists. A
/// client that last saw a lower position has missed events and must reset.
/// </summary>
public sealed record GetOldestPositionQuery : IQuery<long?>;

internal sealed class GetOldestPositionQueryHandler(IJourneyEventRepository repository)
    : IQueryHandler<GetOldestPositionQuery, long?>
{
    public async Task<Result<long?>> Handle(
        GetOldestPositionQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Result.Success(await repository.GetOldestPositionAsync(cancellationToken));
    }
}
