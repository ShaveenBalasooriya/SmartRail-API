using Microsoft.EntityFrameworkCore;
using SmartRail.RailNetwork.Application.Timetable;
using SmartRail.RailNetwork.Contracts;
using SmartRail.RailNetwork.Domain.Abstractions;
using SmartRail.RailNetwork.Infrastructure.Persistence;
using ContractDirection = SmartRail.RailNetwork.Contracts.ServiceDirection;

namespace SmartRail.RailNetwork.Infrastructure.Repositories;

/// <summary>
/// Reads the timetable and its generated journeys from the module's own schema. Stop times are
/// stored as local times of day, so they are converted to UTC instants through the railway clock
/// once the rows are in memory.
/// </summary>
internal sealed class TimetableRepository(RailNetworkDbContext database, IRailwayClock clock)
    : ITimetableRepository
{
    public async Task<ServiceDto?> GetServiceAsync(
        Guid serviceId,
        CancellationToken cancellationToken = default)
    {
        var service = await database.Services
            .AsNoTracking()
            .Where(service => service.Id == serviceId)
            .Select(service => new
            {
                service.Id,
                service.Name,
                service.LineId,
                service.Direction,
                service.GuardStaffId,
                Stops = service.Stops
                    .OrderBy(stop => stop.Sequence)
                    .Select(stop => new ServiceStopDto(
                        stop.Sequence,
                        stop.StationId,
                        stop.IsCalling,
                        stop.ScheduledArrival,
                        stop.ScheduledDeparture,
                        stop.PlatformId))
                    .ToList(),
            })
            .SingleOrDefaultAsync(cancellationToken);

        return service is null
            ? null
            : new ServiceDto(
                service.Id,
                service.Name,
                service.LineId,
                (ContractDirection)service.Direction,
                service.GuardStaffId,
                service.Stops);
    }

    public async Task<IReadOnlyList<JourneyDto>> GetScheduledJourneysAsync(
        DateOnly serviceDate,
        CancellationToken cancellationToken = default)
    {
        var journeys = await database.Journeys
            .AsNoTracking()
            .Where(journey => journey.ServiceDate == serviceDate)
            .OrderBy(journey => journey.ScheduledDepartureAt)
            .ThenBy(journey => journey.Service.Name)
            .Select(journey => new
            {
                journey.Id,
                journey.ServiceId,
                ServiceName = journey.Service.Name,
                journey.Service.LineId,
                LineCode = journey.Service.Line.Code,
                LineName = journey.Service.Line.Name,
                journey.Service.Direction,
                journey.ServiceDate,
                journey.Service.GuardStaffId,
                journey.ScheduledDepartureAt,
                Stops = journey.Service.Stops
                    .OrderBy(stop => stop.Sequence)
                    .Select(stop => new ServiceStopDto(
                        stop.Sequence,
                        stop.StationId,
                        stop.IsCalling,
                        stop.ScheduledArrival,
                        stop.ScheduledDeparture,
                        stop.PlatformId))
                    .ToList(),
            })
            .ToListAsync(cancellationToken);

        return journeys
            .Select(journey => new JourneyDto(
                journey.Id,
                journey.ServiceId,
                journey.ServiceName,
                journey.LineId,
                journey.LineCode.Value,
                journey.LineName,
                (ContractDirection)journey.Direction,
                journey.ServiceDate,
                journey.GuardStaffId,
                journey.ScheduledDepartureAt,
                journey.Stops
                    .Select(stop => new JourneyStopDto(
                        stop.Sequence,
                        stop.StationId,
                        stop.IsCalling,
                        ToUtc(journey.ServiceDate, stop.ScheduledArrival),
                        ToUtc(journey.ServiceDate, stop.ScheduledDeparture),
                        stop.PlatformId))
                    .ToList()))
            .ToList();
    }

    private DateTimeOffset? ToUtc(DateOnly serviceDate, TimeOnly? localTimeOfDay) =>
        localTimeOfDay is { } localTime ? clock.ToUtc(serviceDate, localTime) : null;
}
