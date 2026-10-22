using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using SmartRail.RailNetwork.Application.Journeys;
using SmartRail.RailNetwork.Domain.Entities;
using SmartRail.RailNetwork.Infrastructure.Persistence;

namespace SmartRail.RailNetwork.Infrastructure.Repositories;

/// <summary>
/// Reads the services journeys are generated from and inserts the journeys themselves. The
/// <c>ON CONFLICT DO NOTHING</c> insert lives here, because raw SQL belongs in a repository
/// (design D3, AGENTS.md §3).
/// </summary>
internal sealed class JourneyRepository(RailNetworkDbContext database) : IJourneyRepository
{
    private const string InsertJourneySql =
        """
        INSERT INTO network.journeys (id, service_id, service_date, scheduled_departure_at, created_at)
        VALUES (@id, @service_id, @service_date, @scheduled_departure_at, @created_at)
        ON CONFLICT (service_id, service_date) DO NOTHING
        RETURNING id
        """;

    public async Task<IReadOnlyList<Service>> GetServicesWithStopsAsync(
        CancellationToken cancellationToken = default)
    {
        return await database.Services
            .AsNoTracking()
            .Include(service => service.Stops)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> TryAddAsync(Journey journey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journey);

        var connection = database.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;

        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = InsertJourneySql;

            AddParameter(command, "id", journey.Id);
            AddParameter(command, "service_id", journey.ServiceId);
            AddParameter(command, "service_date", journey.ServiceDate);
            AddParameter(command, "scheduled_departure_at", journey.ScheduledDepartureAt);
            AddParameter(command, "created_at", journey.CreatedAt);

            var result = await command.ExecuteScalarAsync(cancellationToken);

            return result is Guid;
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
