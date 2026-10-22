using SmartRail.RailNetwork.Contracts;
using SmartRail.RailNetwork.Domain.Abstractions;
using SmartRail.SharedKernel.Application.Messaging;
using SmartRail.SharedKernel.Results;

namespace SmartRail.RailNetwork.Application.Timetable.GetScheduledJourneys;

internal sealed class GetScheduledJourneysQueryHandler(
    ITimetableRepository timetable,
    IRailwayClock clock)
    : IQueryHandler<GetScheduledJourneysQuery, IReadOnlyList<JourneyDto>>
{
    public async Task<Result<IReadOnlyList<JourneyDto>>> Handle(
        GetScheduledJourneysQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var serviceDate = request.Date ?? clock.Today;

        return Result.Success(await timetable.GetScheduledJourneysAsync(serviceDate, cancellationToken));
    }
}
