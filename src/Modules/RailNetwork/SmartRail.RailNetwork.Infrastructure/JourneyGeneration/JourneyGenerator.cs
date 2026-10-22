using System.Data;
using System.Data.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartRail.RailNetwork.Application.Clock;
using SmartRail.RailNetwork.Contracts;
using SmartRail.RailNetwork.Domain.Entities;
using SmartRail.RailNetwork.Infrastructure.Persistence;
using ContractServiceDirection = SmartRail.RailNetwork.Contracts.ServiceDirection;
using DomainServiceDirection = SmartRail.RailNetwork.Domain.Entities.ServiceDirection;

namespace SmartRail.RailNetwork.Infrastructure.JourneyGeneration;

/// <summary>
/// Turns every service into one dated journey for today and one for tomorrow (design D2, D3).
/// Rows are inserted with <c>ON CONFLICT DO NOTHING</c>, so re-running generation is idempotent and
/// only the rows PostgreSQL actually creates are announced.
/// </summary>
public sealed class JourneyGenerator(
    RailNetworkDbContext context,
    RailwayClock clock,
    IPublisher publisher,
    TimeProvider timeProvider,
    ILogger<JourneyGenerator> logger)
{
    private const string InsertJourneySql =
        """
        INSERT INTO network.journeys (id, service_id, service_date, scheduled_departure_at, created_at)
        VALUES (@id, @service_id, @service_date, @scheduled_departure_at, @created_at)
        ON CONFLICT (service_id, service_date) DO NOTHING
        RETURNING id
        """;

    /// <summary>
    /// Generates today's and tomorrow's journeys for every service. With no services in the
    /// database this does nothing.
    /// </summary>
    public async Task GenerateAsync(CancellationToken cancellationToken = default)
    {
        var services = await context.Services
            .AsNoTracking()
            .Include(service => service.Stops)
            .ToListAsync(cancellationToken);

        if (services.Count == 0)
        {
            return;
        }

        var dates = new[] { clock.Today, clock.Tomorrow };
        var created = new List<JourneyGenerated>();

        var connection = context.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;

        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            foreach (var service in services)
            {
                var firstStop = service.Stops
                    .OrderBy(stop => stop.Sequence)
                    .FirstOrDefault();

                if (firstStop?.ScheduledDeparture is not { } firstDeparture)
                {
                    logger.LogWarning(
                        "Service {ServiceId} has no first-stop departure; its journeys were not generated",
                        service.Id);

                    continue;
                }

                foreach (var date in dates)
                {
                    var journeyId = await InsertAsync(
                        connection,
                        service.Id,
                        date,
                        clock.ToUtc(date, firstDeparture),
                        timeProvider.GetUtcNow(),
                        cancellationToken);

                    if (journeyId is null)
                    {
                        continue;
                    }

                    created.Add(new JourneyGenerated(
                        journeyId.Value,
                        service.Id,
                        date,
                        service.LineId,
                        MapDirection(service.Direction),
                        service.GuardStaffId,
                        MapStops(service.Stops, date)));
                }
            }
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }

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
    }

    private static async Task<Guid?> InsertAsync(
        DbConnection connection,
        Guid serviceId,
        DateOnly serviceDate,
        DateTimeOffset scheduledDepartureAt,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = InsertJourneySql;

        AddParameter(command, "id", Guid.CreateVersion7());
        AddParameter(command, "service_id", serviceId);
        AddParameter(command, "service_date", serviceDate);
        AddParameter(command, "scheduled_departure_at", scheduledDepartureAt);
        AddParameter(command, "created_at", createdAt);

        var result = await command.ExecuteScalarAsync(cancellationToken);

        return result is Guid journeyId ? journeyId : null;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private JourneyStopDto[] MapStops(
        IEnumerable<ServiceStop> stops,
        DateOnly serviceDate)
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

    private static ContractServiceDirection MapDirection(DomainServiceDirection direction)
    {
        return direction switch
        {
            DomainServiceDirection.Outbound => ContractServiceDirection.Outbound,
            DomainServiceDirection.Inbound => ContractServiceDirection.Inbound,
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unknown service direction."),
        };
    }
}
