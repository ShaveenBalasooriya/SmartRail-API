using MediatR;
using Microsoft.Extensions.Logging;
using SmartRail.RailNetwork.Application.Abstractions;
using SmartRail.RailNetwork.Contracts;
using SmartRail.RailNetwork.Domain.Abstractions;
using SmartRail.RailNetwork.Domain.Entities;
using SmartRail.SharedKernel.Application.Messaging;
using SmartRail.SharedKernel.Results;
using ContractDirection = SmartRail.RailNetwork.Contracts.ServiceDirection;
using DomainDirection = SmartRail.RailNetwork.Domain.Entities.ServiceDirection;

namespace SmartRail.RailNetwork.Application.Journeys.GenerateJourneys;

/// <summary>
/// Turns every service into one journey for today and one for tomorrow (journey-generation spec).
/// The insert is idempotent, so only the rows the repository actually created are announced, and
/// the announcement happens after the unit of work has committed (design D3).
/// </summary>
internal sealed class GenerateJourneysCommandHandler(
    IJourneyRepository journeys,
    IUnitOfWork unitOfWork,
    IRailwayClock clock,
    IPublisher publisher,
    ILogger<GenerateJourneysCommandHandler> logger) : ICommandHandler<GenerateJourneysCommand>
{
    public async Task<Result> Handle(
        GenerateJourneysCommand request,
        CancellationToken cancellationToken)
    {
        var services = await journeys.GetServicesWithStopsAsync(cancellationToken);

        if (services.Count == 0)
        {
            return Result.Success();
        }

        DateOnly[] dates = [clock.Today, clock.Tomorrow];
        var created = new List<JourneyGenerated>();

        foreach (var service in services)
        {
            foreach (var date in dates)
            {
                var journey = Journey.Create(service, date, clock);

                if (journey.IsFailure)
                {
                    logger.LogWarning(
                        "Service {ServiceId} has no first-stop departure; its journeys were not generated",
                        service.Id);

                    break;
                }

                if (!await journeys.TryAddAsync(journey.Value, cancellationToken))
                {
                    continue;
                }

                created.Add(new JourneyGenerated(
                    journey.Value.Id,
                    service.Id,
                    date,
                    service.LineId,
                    MapDirection(service.Direction),
                    service.GuardStaffId,
                    MapStops(service.Stops, date)));
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        foreach (var journey in created)
        {
            await publisher.Publish(journey, cancellationToken);
        }

        if (created.Count > 0)
        {
            logger.LogInformation(
                "Journey generation created {JourneyCount} journey(s) for {Today} and {Tomorrow}",
                created.Count,
                clock.Today,
                clock.Tomorrow);
        }

        return Result.Success();
    }

    private JourneyStopDto[] MapStops(IEnumerable<ServiceStop> stops, DateOnly serviceDate)
    {
        return stops
            .OrderBy(stop => stop.Sequence)
            .Select(stop => new JourneyStopDto(
                stop.Sequence,
                stop.StationId,
                stop.IsCalling,
                stop.ScheduledArrival is { } arrival ? clock.ToUtc(serviceDate, arrival) : null,
                stop.ScheduledDeparture is { } departure ? clock.ToUtc(serviceDate, departure) : null,
                stop.PlatformId))
            .ToArray();
    }

    private static ContractDirection MapDirection(DomainDirection direction)
    {
        return direction switch
        {
            DomainDirection.Outbound => ContractDirection.Outbound,
            DomainDirection.Inbound => ContractDirection.Inbound,
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unknown service direction."),
        };
    }
}
