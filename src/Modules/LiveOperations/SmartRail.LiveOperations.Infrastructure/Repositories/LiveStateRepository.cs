using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SmartRail.LiveOperations.Application.LiveState;
using SmartRail.LiveOperations.Domain.LiveState;
using SmartRail.LiveOperations.Infrastructure.Persistence;

namespace SmartRail.LiveOperations.Infrastructure.Repositories;

/// <summary>
/// The live-state persistence. Both inserts use <c>ON CONFLICT DO NOTHING</c>, so the announcement
/// handler and the start-up backfill can run at the same time without either failing or overwriting
/// the other's row (design D4). The row lock that serialises facts for one journey also lives here,
/// and the completion check reads the running journeys from here without tracking them.
/// </summary>
internal sealed class LiveStateRepository(LiveOperationsDbContext database) : ILiveStateRepository
{
    private const string LockSql =
        """
        SELECT journey_id
        FROM operations.journey_live_states
        WHERE journey_id = @journey_id
        FOR UPDATE
        """;

    // The state of a journey with no facts is always 'scheduled'; that is the only status a
    // created state can have.
    private const string InsertJourneySql =
        """
        INSERT INTO operations.journey_live_states
            (journey_id, service_id, line_id, service_date, guard_staff_id, lifecycle_status,
             delay_minutes, last_departed_station_id, next_station_id, last_sequence_number,
             last_updated_at)
        VALUES
            (@journey_id, @service_id, @line_id, @service_date, @guard_staff_id, 'scheduled',
             @delay_minutes, @last_departed_station_id, @next_station_id, @last_sequence_number,
             @last_updated_at)
        ON CONFLICT (journey_id) DO NOTHING
        """;

    private const string InsertStopSql =
        """
        INSERT INTO operations.journey_stop_live_states
            (id, journey_id, sequence, station_id, is_calling, scheduled_arrival,
             scheduled_departure, expected_arrival, expected_departure, actual_departure,
             platform_id, platform_changed_at)
        VALUES
            (@id, @journey_id, @sequence, @station_id, @is_calling, @scheduled_arrival,
             @scheduled_departure, @expected_arrival, @expected_departure, @actual_departure,
             @platform_id, @platform_changed_at)
        ON CONFLICT (journey_id, sequence) DO NOTHING
        """;

    public async Task<JourneyLiveState?> GetForUpdateAsync(
        Guid journeyId,
        CancellationToken cancellationToken = default)
    {
        var connection = database.Database.GetDbConnection();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = LockSql;
            command.Transaction = database.Database.CurrentTransaction?.GetDbTransaction();
            AddParameter(command, "journey_id", journeyId);

            if (await command.ExecuteScalarAsync(cancellationToken) is not Guid)
            {
                return null;
            }
        }

        return await database.JourneyLiveStates
            .Include(state => state.Stops)
            .SingleAsync(state => state.Id == journeyId, cancellationToken);
    }

    public async Task<bool> CreateIfMissingAsync(
        JourneyLiveState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        // Consumer and backfill only, never inside a request: EF Core cannot begin a transaction on
        // a context that has already enlisted in the request's one.
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var connection = database.Database.GetDbConnection();

        int inserted;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = InsertJourneySql;
            command.Transaction = database.Database.CurrentTransaction?.GetDbTransaction();

            AddParameter(command, "journey_id", state.Id);
            AddParameter(command, "service_id", state.ServiceId);
            AddParameter(command, "line_id", state.LineId);
            AddParameter(command, "service_date", state.ServiceDate);
            AddParameter(command, "guard_staff_id", state.GuardStaffId);
            AddParameter(command, "delay_minutes", state.DelayMinutes);
            AddParameter(command, "last_departed_station_id", state.LastDepartedStationId);
            AddParameter(command, "next_station_id", state.NextStationId);
            AddParameter(command, "last_sequence_number", state.LastSequenceNumber);
            AddParameter(command, "last_updated_at", state.LastUpdatedAt);

            inserted = await command.ExecuteNonQueryAsync(cancellationToken);
        }

        if (inserted == 0)
        {
            // Lost the race with the other creator; its transaction also wrote the stop rows.
            return false;
        }

        foreach (var stop in state.Stops.OrderBy(stop => stop.Sequence))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = InsertStopSql;
            command.Transaction = database.Database.CurrentTransaction?.GetDbTransaction();

            AddParameter(command, "id", stop.Id);
            AddParameter(command, "journey_id", stop.JourneyId);
            AddParameter(command, "sequence", stop.Sequence);
            AddParameter(command, "station_id", stop.StationId);
            AddParameter(command, "is_calling", stop.IsCalling);
            AddParameter(command, "scheduled_arrival", stop.ScheduledArrival);
            AddParameter(command, "scheduled_departure", stop.ScheduledDeparture);
            AddParameter(command, "expected_arrival", stop.ExpectedArrival);
            AddParameter(command, "expected_departure", stop.ExpectedDeparture);
            AddParameter(command, "actual_departure", stop.ActualDeparture);
            AddParameter(command, "platform_id", stop.PlatformId);
            AddParameter(command, "platform_changed_at", stop.PlatformChangedAt);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    public void Append(JourneyEvent journeyEvent)
    {
        ArgumentNullException.ThrowIfNull(journeyEvent);

        database.JourneyEvents.Add(journeyEvent);
    }

    public async Task<IReadOnlyList<Guid>> GetRunningJourneyIdsAsync(
        CancellationToken cancellationToken = default)
    {
        return await database.JourneyLiveStates
            .AsNoTracking()
            .Where(state => state.Status == JourneyLifecycleStatus.Running)
            .OrderBy(state => state.Id)
            .Select(state => state.Id)
            .ToListAsync(cancellationToken);
    }

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
