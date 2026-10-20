using MediatR;
using SmartRail.RailNetwork.Application.Timetable.GetScheduledJourneys;
using SmartRail.RailNetwork.Application.Timetable.GetService;
using SmartRail.RailNetwork.Contracts;

namespace SmartRail.RailNetwork.Application.Timetable;

/// <summary>
/// Implements the module's timetable contract by sending its own queries (design D3, architecture
/// §3). The contract reports an unknown service as null and an unacceptable date as no journeys, so
/// a caller never receives a failure it has no way to act on.
/// </summary>
internal sealed class TimetableService(ISender sender) : ITimetable
{
    public async Task<ServiceDto?> GetServiceAsync(
        Guid serviceId,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(new GetServiceQuery(serviceId), cancellationToken);

        return result.IsSuccess ? result.Value : null;
    }

    public async Task<IReadOnlyList<JourneyDto>> GetScheduledJourneysAsync(
        DateOnly serviceDate,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(new GetScheduledJourneysQuery(serviceDate), cancellationToken);

        return result.IsSuccess ? result.Value : [];
    }
}
