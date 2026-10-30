using MediatR;
using SmartRail.Host.IntegrationTests.Infrastructure;
using SmartRail.LiveOperations.Application.Departures;
using SmartRail.LiveOperations.Application.LiveState;
using SmartRail.LiveOperations.Application.LiveState.ApplyJourneyFact;
using SmartRail.LiveOperations.Domain.Calculation;
using SmartRail.LiveOperations.Domain.Departures;
using SmartRail.LiveOperations.Infrastructure.Persistence;
using SmartRail.RailNetwork.Contracts;
using SmartRail.RailNetwork.Infrastructure.Persistence;
using SmartRail.SharedKernel.Application.Abstractions;
using SmartRail.SharedKernel.Application.Events;
using SmartRail.SharedKernel.Results;
using ContractJourneyEventType = SmartRail.LiveOperations.Contracts.JourneyEventType;

namespace SmartRail.Host.IntegrationTests.LiveOperations;

/// <summary>
/// The <c>operations.departure_logs</c> table and the fact reader that replaces the empty departure
/// side (departure-logging tasks 2.1 and 2.2): the migration and its constraints, the commit order
/// shared with <c>status_changes</c>, a stored log becoming a departure fact that sets the stop's
/// actual departure and appends one <c>departure_logged</c> event, the unique stop index refusing a
/// second log without poisoning the request transaction, and the device time staying separate from
/// the receipt time of an offline log.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DepartureLogTests
{
    /// <summary>The seeded controller account (seed/identity/staff.json).</summary>
    private static readonly Guid ControllerStaffId = Guid.Parse("01a15874-e69c-70da-a689-76a7268ed330");

    /// <summary>The seeded guard account (seed/identity/staff.json).</summary>
    private static readonly Guid GuardStaffId = Guid.Parse("01a15874-e69d-73b2-b24d-d85d22fdead2");

    private static readonly DateTimeOffset DepartAt = new(2026, 10, 28, 8, 0, 0, TimeSpan.Zero);

    private readonly PostgresFixture _postgres;

    public DepartureLogTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task The_migration_creates_the_departure_log_table_its_constraints_and_its_shared_sequence()
    {
        using var factory = new SmartRailApiFactory(_postgres.ConnectionString);
        await StartAsync(factory);

        var tables = await _postgres.QueryAsync(
            "SELECT to_regclass('operations.departure_logs')::text",
            reader => reader.IsDBNull(0) ? null : reader.GetString(0));
        Assert.Equal(["operations.departure_logs"], tables);

        var sequence = await _postgres.QueryAsync(
            "SELECT sequencename FROM pg_sequences WHERE schemaname = 'operations' AND sequencename = 'fact_order_seq'",
            reader => reader.GetString(0));
        Assert.Equal(["fact_order_seq"], sequence);

        var defaultValue = Assert.Single(await _postgres.QueryAsync(
            """
            SELECT column_default
            FROM information_schema.columns
            WHERE table_schema = 'operations'
              AND table_name = 'departure_logs'
              AND column_name = 'fact_order'
            """,
            reader => reader.GetString(0)));
        Assert.Contains("nextval", defaultValue, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fact_order_seq", defaultValue, StringComparison.Ordinal);

        var checks = await _postgres.QueryAsync(
            """
            SELECT conname
            FROM pg_constraint
            WHERE conrelid = 'operations.departure_logs'::regclass
              AND contype = 'c'
            ORDER BY conname
            """,
            reader => reader.GetString(0));
        Assert.Equal(["CK_departure_logs_delay_cause"], checks);

        var foreignKeys = await _postgres.QueryAsync(
            """
            SELECT conname
            FROM pg_constraint
            WHERE conrelid = 'operations.departure_logs'::regclass
              AND contype = 'f'
            ORDER BY conname
            """,
            reader => reader.GetString(0));
        Assert.Equal(["FK_departure_logs_journey_live_states_journey_id"], foreignKeys);

        var uniqueIndex = Assert.Single(await _postgres.QueryAsync(
            """
            SELECT indexdef
            FROM pg_indexes
            WHERE schemaname = 'operations'
              AND tablename = 'departure_logs'
              AND indexname = 'IX_departure_logs_journey_id_stop_sequence'
            """,
            reader => reader.GetString(0)));
        Assert.StartsWith("CREATE UNIQUE INDEX", uniqueIndex, StringComparison.Ordinal);

        var columns = await _postgres.QueryAsync(
            """
            SELECT column_name, data_type, is_nullable
            FROM information_schema.columns
            WHERE table_schema = 'operations'
              AND table_name = 'departure_logs'
              AND column_name IN ('departed_at', 'received_at', 'delay_cause')
            ORDER BY column_name
            """,
            reader => (Name: reader.GetString(0), Type: reader.GetString(1), Nullable: reader.GetString(2)));

        Assert.Equal(["delay_cause", "departed_at", "received_at"], columns.Select(column => column.Name));
        Assert.Equal("text", columns[0].Type);
        Assert.Equal("YES", columns[0].Nullable);
        Assert.All(columns.Skip(1), column =>
        {
            Assert.Equal("timestamp with time zone", column.Type);
            Assert.Equal("NO", column.Nullable);
        });
    }

    [Fact]
    public async Task The_fact_order_comes_from_the_sequence_the_status_changes_use()
    {
        using var factory = new SmartRailApiFactory(_postgres.ConnectionString);
        await StartAsync(factory);

        var journeyId = await CreateLiveJourneyAsync(factory);

        try
        {
            var changeId = Guid.CreateVersion7();
            await InsertCancellationAsync(journeyId, changeId, DepartAt.AddMinutes(-5));

            var logId = Guid.CreateVersion7();
            await InsertLogAsync(journeyId, logId, stopSequence: 1, DepartAt.AddMinutes(2), DepartAt.AddMinutes(5));

            var changeOrder = Assert.Single(await _postgres.QueryAsync(
                $"SELECT fact_order FROM operations.status_changes WHERE id = '{changeId}'",
                reader => reader.GetInt64(0)));

            var departureOrder = Assert.Single(await _postgres.QueryAsync(
                $"SELECT fact_order FROM operations.departure_logs WHERE id = '{logId}'",
                reader => reader.GetInt64(0)));

            Assert.True(changeOrder > 0);
            Assert.True(departureOrder > changeOrder);
        }
        finally
        {
            await DeleteJourneyAsync(journeyId);
        }
    }

    [Fact]
    public async Task A_stored_log_becomes_a_departure_fact_and_one_departure_logged_event()
    {
        using var factory = new SmartRailApiFactory(_postgres.ConnectionString);
        await StartAsync(factory);

        var journeyId = await CreateLiveJourneyAsync(factory);

        try
        {
            var departedAt = DepartAt.AddMinutes(2);
            var receivedAt = DepartAt.AddMinutes(25);
            var logId = Guid.CreateVersion7();
            await InsertLogAsync(journeyId, logId, stopSequence: 1, departedAt, receivedAt, cause: "crew");

            var departure = Assert.IsType<DepartureFact>(Assert.Single(await ReadFactsAsync(factory, journeyId)));
            Assert.Equal(1, departure.StopSequence);
            Assert.Equal(departedAt, departure.DepartedAt);
            Assert.Equal(receivedAt, departure.RecordedAt);
            Assert.True(departure.StreamOrder > 0);

            var applied = await SendAsync(factory, new ApplyJourneyFactCommand(
                journeyId,
                Fact: null,
                ContractJourneyEventType.DepartureLogged,
                logId,
                departedAt,
                receivedAt));
            Assert.True(applied.IsSuccess);

            var actualDeparture = Assert.Single(await _postgres.QueryAsync(
                $"""
                SELECT actual_departure
                FROM operations.journey_stop_live_states
                WHERE journey_id = '{journeyId}' AND sequence = 1
                """,
                reader => reader.GetFieldValue<DateTimeOffset>(0)));
            Assert.Equal(departedAt, actualDeparture);

            var events = await _postgres.QueryAsync(
                $"""
                SELECT event_type, departure_log_id
                FROM operations.journey_events
                WHERE journey_id = '{journeyId}'
                """,
                reader => (Type: reader.GetString(0), LogId: reader.GetGuid(1)));

            var logged = Assert.Single(events);
            Assert.Equal("departure_logged", logged.Type);
            Assert.Equal(logId, logged.LogId);

            var status = Assert.Single(await _postgres.QueryAsync(
                $"SELECT lifecycle_status FROM operations.journey_live_states WHERE journey_id = '{journeyId}'",
                reader => reader.GetString(0)));
            Assert.Equal("running", status);
        }
        finally
        {
            await DeleteJourneyAsync(journeyId);
        }
    }

    [Fact]
    public async Task A_second_log_for_the_same_stop_is_refused_without_poisoning_the_transaction()
    {
        using var factory = new SmartRailApiFactory(_postgres.ConnectionString);
        await StartAsync(factory);

        var journeyId = await CreateLiveJourneyAsync(factory);

        try
        {
            using var scope = factory.Services.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IDepartureLogRepository>();
            var database = scope.ServiceProvider.GetRequiredService<LiveOperationsDbContext>();

            await using var transaction = await database.Database.BeginTransactionAsync();

            Assert.True(await repository.AddAsync(
                NewLog(journeyId, stopSequence: 1, DepartAt.AddMinutes(2))));

            // The same stop with a different device identifier: the unique index refuses it, and the
            // savepoint keeps the transaction usable for the rest of the request.
            Assert.False(await repository.AddAsync(
                NewLog(journeyId, stopSequence: 1, DepartAt.AddMinutes(3))));

            Assert.True(await repository.AddAsync(
                NewLog(journeyId, stopSequence: 2, DepartAt.AddMinutes(13))));

            await transaction.RollbackAsync();

            var stored = Assert.Single(await _postgres.QueryAsync(
                $"SELECT count(*) FROM operations.departure_logs WHERE journey_id = '{journeyId}'",
                reader => reader.GetInt64(0)));
            Assert.Equal(0, stored);
        }
        finally
        {
            await DeleteJourneyAsync(journeyId);
        }
    }

    [Fact]
    public async Task Offline_departures_keep_their_device_times_and_record_the_receipt_time()
    {
        using var factory = new SmartRailApiFactory(_postgres.ConnectionString);
        await StartAsync(factory);

        var journeyId = await CreateLiveJourneyAsync(factory);

        try
        {
            var firstAt = DepartAt.AddMinutes(2);
            var secondAt = DepartAt.AddMinutes(9);
            var receivedAt = DepartAt.AddMinutes(25);
            var firstId = Guid.CreateVersion7();
            var secondId = Guid.CreateVersion7();

            await InsertLogAsync(journeyId, firstId, stopSequence: 1, firstAt, receivedAt);
            await InsertLogAsync(journeyId, secondId, stopSequence: 2, secondAt, receivedAt);

            var rows = await _postgres.QueryAsync(
                $"""
                SELECT stop_sequence, departed_at, received_at
                FROM operations.departure_logs
                WHERE journey_id = '{journeyId}'
                ORDER BY stop_sequence
                """,
                reader => (
                    StopSequence: reader.GetInt32(0),
                    DepartedAt: reader.GetFieldValue<DateTimeOffset>(1),
                    ReceivedAt: reader.GetFieldValue<DateTimeOffset>(2)));

            Assert.Equal(2, rows.Count);
            Assert.Equal(firstAt, rows[0].DepartedAt);
            Assert.Equal(secondAt, rows[1].DepartedAt);
            Assert.All(rows, row => Assert.Equal(receivedAt, row.ReceivedAt));

            var departures = (await ReadFactsAsync(factory, journeyId))
                .OfType<DepartureFact>()
                .OrderBy(fact => fact.StopSequence)
                .ToArray();

            Assert.Equal(2, departures.Length);
            Assert.Equal(firstAt, departures[0].DepartedAt);
            Assert.Equal(secondAt, departures[1].DepartedAt);
            Assert.All(departures, fact => Assert.Equal(receivedAt, fact.RecordedAt));

            Assert.True((await SendAsync(factory, new ApplyJourneyFactCommand(
                journeyId,
                Fact: null,
                ContractJourneyEventType.DepartureLogged,
                firstId,
                firstAt,
                receivedAt))).IsSuccess);
            Assert.True((await SendAsync(factory, new ApplyJourneyFactCommand(
                journeyId,
                Fact: null,
                ContractJourneyEventType.DepartureLogged,
                secondId,
                secondAt,
                receivedAt))).IsSuccess);

            var actualDepartures = await _postgres.QueryAsync(
                $"""
                SELECT actual_departure
                FROM operations.journey_stop_live_states
                WHERE journey_id = '{journeyId}' AND sequence IN (1, 2)
                ORDER BY sequence
                """,
                reader => reader.GetFieldValue<DateTimeOffset>(0));

            Assert.Equal([firstAt, secondAt], actualDepartures);
        }
        finally
        {
            await DeleteJourneyAsync(journeyId);
        }
    }

    private static async Task StartAsync(SmartRailApiFactory factory)
    {
        using var client = factory.CreateApiClient();
        var ready = await client.GetAsync("/health/live");
        Assert.Equal(System.Net.HttpStatusCode.OK, ready.StatusCode);
    }

    /// <summary>Creates the journey's live state through the real generator announcement.</summary>
    private async Task<Guid> CreateLiveJourneyAsync(SmartRailApiFactory factory)
    {
        await StartAsync(factory);

        var journeyId = Guid.CreateVersion7();

        using var scope = factory.Services.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredKeyedService<IIntegrationEventPublisher>(
            IntegrationEventPublisherKeys.Network);
        var database = scope.ServiceProvider.GetRequiredService<RailNetworkDbContext>();

        publisher.Publish(
            new JourneyGenerated(
                journeyId,
                Guid.CreateVersion7(),
                new DateOnly(2026, 10, 28),
                Guid.CreateVersion7(),
                ServiceDirection.Outbound,
                GuardStaffId,
                [
                    new JourneyStopDto(1, Guid.CreateVersion7(), true, null, DepartAt, Guid.CreateVersion7()),
                    new JourneyStopDto(2, Guid.CreateVersion7(), true, DepartAt.AddMinutes(10), DepartAt.AddMinutes(11), Guid.CreateVersion7()),
                    new JourneyStopDto(3, Guid.CreateVersion7(), true, DepartAt.AddMinutes(20), null, null),
                ]),
            journeyId);

        await database.SaveChangesAsync();

        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (await CountLiveStatesAsync(journeyId) == 0 && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        Assert.Equal(1, await CountLiveStatesAsync(journeyId));

        return journeyId;
    }

    private static async Task<IReadOnlyList<Fact>> ReadFactsAsync(
        SmartRailApiFactory factory,
        Guid journeyId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider
            .GetRequiredService<IJourneyFactReader>()
            .GetFactsAsync(journeyId);
    }

    private static async Task<Result> SendAsync(
        SmartRailApiFactory factory,
        ApplyJourneyFactCommand command)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(command);
    }

    private static DepartureLog NewLog(Guid journeyId, int stopSequence, DateTimeOffset departedAt)
    {
        var result = DepartureLog.Create(
            Guid.CreateVersion7(),
            journeyId,
            stopSequence,
            Guid.CreateVersion7(),
            GuardStaffId,
            departedAt,
            DelayCause.Other,
            DepartAt.AddMinutes(5));

        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private async Task<int> CountLiveStatesAsync(Guid journeyId)
    {
        var count = Assert.Single(await _postgres.QueryAsync(
            $"SELECT count(*) FROM operations.journey_live_states WHERE journey_id = '{journeyId}'",
            reader => reader.GetInt64(0)));

        return (int)count;
    }

    private Task<int> InsertLogAsync(
        Guid journeyId,
        Guid logId,
        int stopSequence,
        DateTimeOffset departedAt,
        DateTimeOffset receivedAt,
        string cause = "other") =>
        _postgres.ExecuteAsync(
            $"""
            INSERT INTO operations.departure_logs
                (id, journey_id, stop_sequence, station_id, guard_staff_id, departed_at, delay_cause, received_at)
            VALUES
                ('{logId}', '{journeyId}', {stopSequence}, '{Guid.CreateVersion7()}', '{GuardStaffId}',
                 '{departedAt:O}', '{cause}', '{receivedAt:O}')
            """);

    private Task<int> InsertCancellationAsync(Guid journeyId, Guid changeId, DateTimeOffset publishedAt) =>
        _postgres.ExecuteAsync(
            $"""
            INSERT INTO operations.status_changes
                (id, journey_id, change_type, provenance, published_by, published_at)
            VALUES
                ('{changeId}', '{journeyId}', 'cancellation',
                 'automatically_published', '{ControllerStaffId}', '{publishedAt:O}')
            """);

    /// <summary>Removes every row the test created for the journey.</summary>
    private async Task DeleteJourneyAsync(Guid journeyId)
    {
        await _postgres.ExecuteAsync(
            $"DELETE FROM operations.departure_logs WHERE journey_id = '{journeyId}'");
        await _postgres.ExecuteAsync(
            $"""
            DELETE FROM operations.status_changes
            WHERE journey_id = '{journeyId}' AND corrects_change_id IS NOT NULL
            """);
        await _postgres.ExecuteAsync(
            $"DELETE FROM operations.status_changes WHERE journey_id = '{journeyId}'");
        await _postgres.ExecuteAsync(
            $"""
            DELETE FROM operations.outbox_deliveries
            WHERE message_id IN (
                SELECT id FROM operations.outbox_messages WHERE ordering_key = '{journeyId}')
            """);
        await _postgres.ExecuteAsync(
            $"DELETE FROM operations.outbox_messages WHERE ordering_key = '{journeyId}'");
        await _postgres.ExecuteAsync(
            $"DELETE FROM operations.journey_events WHERE journey_id = '{journeyId}'");
        await _postgres.ExecuteAsync(
            $"DELETE FROM operations.journey_stop_live_states WHERE journey_id = '{journeyId}'");
        await _postgres.ExecuteAsync(
            $"DELETE FROM operations.journey_live_states WHERE journey_id = '{journeyId}'");
    }
}
