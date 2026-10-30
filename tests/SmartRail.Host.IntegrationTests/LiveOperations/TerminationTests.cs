using MediatR;
using Microsoft.Extensions.DependencyInjection;
using SmartRail.Host.IntegrationTests.Infrastructure;
using SmartRail.LiveOperations.Application.LiveState.ApplyJourneyFact;
using SmartRail.LiveOperations.Contracts;
using SmartRail.LiveOperations.Domain.Calculation;
using SmartRail.RailNetwork.Contracts;
using SmartRail.RailNetwork.Infrastructure.Persistence;
using SmartRail.SharedKernel.Application.Events;
using SmartRail.SharedKernel.Results;

namespace SmartRail.Host.IntegrationTests.LiveOperations;

/// <summary>
/// Early termination stored as a <c>status_changes</c> row (controller-operations D7, task 2.6): the
/// fact reader turns it into a <see cref="ChangeKind.Termination"/> fact, the calculator makes the
/// journey <c>terminated</c> with no expected times after the last served stop, and the migration's
/// widened event-type constraint lets the <c>termination_published</c> journey event and its outbox
/// row be written. A correction row then withdraws the termination and the journey runs again.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TerminationTests
{
    /// <summary>The seeded controller account (seed/identity/staff.json).</summary>
    private static readonly Guid ControllerStaffId = Guid.Parse("01a15874-e69c-70da-a689-76a7268ed330");

    /// <summary>The seeded guard account (seed/identity/staff.json).</summary>
    private static readonly Guid GuardStaffId = Guid.Parse("01a15874-e69d-73b2-b24d-d85d22fdead2");

    private static readonly DateTimeOffset DepartAt = new(2026, 10, 28, 8, 0, 0, TimeSpan.Zero);

    private readonly PostgresFixture _postgres;

    public TerminationTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task A_stored_termination_gives_the_terminated_state_and_a_correction_returns_it_to_running()
    {
        using var factory = new SmartRailApiFactory(_postgres.ConnectionString);
        var journeyId = await CreateLiveJourneyAsync(factory);

        try
        {
            // The constraint the migration widened admits the new event type; the inserts below
            // would be rejected by the old list.
            var constraint = Assert.Single(await _postgres.QueryAsync(
                """
                SELECT pg_get_constraintdef(oid)
                FROM pg_constraint
                WHERE conname = 'CK_journey_events_event_type'
                """,
                reader => reader.GetString(0)));
            Assert.Contains("termination_published", constraint, StringComparison.Ordinal);

            var publishedAt = DepartAt.AddMinutes(5);
            var terminationId = Guid.CreateVersion7();
            await InsertTerminationAsync(journeyId, terminationId, lastStopSequence: 2, publishedAt);

            // The departure log table arrives with add-departure-logging, so the departure fact is
            // supplied with the command; the reader cannot return it yet (design D2).
            var departure = new DepartureFact(1, DepartAt, DepartAt, StreamOrder: 1);

            var terminated = await SendAsync(factory, new ApplyJourneyFactCommand(
                journeyId,
                departure,
                JourneyEventType.TerminationPublished,
                terminationId,
                publishedAt,
                publishedAt));
            Assert.True(terminated.IsSuccess);

            var state = await ReadLiveStateAsync(journeyId);

            Assert.Equal("terminated", state.Status);
            Assert.Null(state.DelayMinutes);
            Assert.Null(state.NextStationId);
            Assert.Equal(DepartAt, state.Stops[0].ActualDeparture);

            // The last stop served still has its expected arrival; the stop after it keeps only its
            // scheduled time and reports no expected time.
            Assert.NotNull(state.Stops[1].ExpectedArrival);
            Assert.Equal(DepartAt.AddMinutes(10), state.Stops[1].ExpectedArrival);
            Assert.Equal(DepartAt.AddMinutes(20), state.Stops[2].ScheduledArrival);
            Assert.Null(state.Stops[2].ExpectedArrival);
            Assert.Null(state.Stops[2].ExpectedDeparture);

            Assert.Equal(
                ["termination_published"],
                await EventTypesAsync(journeyId));
            Assert.Equal(
                ["JourneyStateChanged"],
                await _postgres.QueryAsync(
                    $"SELECT event_type FROM operations.outbox_messages WHERE ordering_key = '{journeyId}'",
                    reader => reader.GetString(0)));

            // A correction withdraws the termination: the journey returns to running from the
            // departure it already has (change-correction spec: wrong early termination).
            var correctionId = Guid.CreateVersion7();
            await InsertCorrectionAsync(journeyId, correctionId, terminationId, publishedAt.AddMinutes(1));

            var corrected = await SendAsync(factory, new ApplyJourneyFactCommand(
                journeyId,
                departure,
                JourneyEventType.CorrectionPublished,
                correctionId,
                publishedAt.AddMinutes(1),
                publishedAt.AddMinutes(1)));
            Assert.True(corrected.IsSuccess);

            state = await ReadLiveStateAsync(journeyId);

            Assert.Equal("running", state.Status);
            Assert.Equal(state.Stops[1].StationId, state.NextStationId);
            Assert.Equal(DepartAt.AddMinutes(20), state.Stops[2].ExpectedArrival);

            Assert.Equal(
                ["termination_published", "correction_published"],
                await EventTypesAsync(journeyId));
        }
        finally
        {
            await DeleteJourneyAsync(journeyId);
        }
    }

    private static async Task StartAsync(SmartRailApiFactory factory)
    {
        using var client = factory.CreateApiClient();
        using var response = await client.GetAsync("/health/live");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
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
        while (await CountAsync(journeyId) == 0 && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        Assert.Equal(1, await CountAsync(journeyId));

        return journeyId;
    }

    private static async Task<Result> SendAsync(
        SmartRailApiFactory factory,
        ApplyJourneyFactCommand command)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(command);
    }

    private async Task<int> CountAsync(Guid journeyId)
    {
        var count = Assert.Single(await _postgres.QueryAsync(
            $"SELECT count(*) FROM operations.journey_live_states WHERE journey_id = '{journeyId}'",
            reader => reader.GetInt64(0)));

        return (int)count;
    }

    private Task<List<string>> EventTypesAsync(Guid journeyId) =>
        _postgres.QueryAsync(
            $"""
            SELECT event_type
            FROM operations.journey_events
            WHERE journey_id = '{journeyId}'
            ORDER BY sequence_number
            """,
            reader => reader.GetString(0));

    private Task<int> InsertTerminationAsync(
        Guid journeyId,
        Guid changeId,
        int lastStopSequence,
        DateTimeOffset publishedAt) =>
        _postgres.ExecuteAsync(
            $"""
            INSERT INTO operations.status_changes
                (id, journey_id, change_type, stop_sequence, provenance, published_by, published_at)
            VALUES
                ('{changeId}', '{journeyId}', 'termination', {lastStopSequence},
                 'automatically_published', '{ControllerStaffId}', '{publishedAt:O}')
            """);

    private Task<int> InsertCorrectionAsync(
        Guid journeyId,
        Guid changeId,
        Guid correctsChangeId,
        DateTimeOffset publishedAt) =>
        _postgres.ExecuteAsync(
            $"""
            INSERT INTO operations.status_changes
                (id, journey_id, change_type, corrects_change_id, provenance, published_by, published_at)
            VALUES
                ('{changeId}', '{journeyId}', 'correction', '{correctsChangeId}',
                 'automatically_published', '{ControllerStaffId}', '{publishedAt:O}')
            """);

    private async Task<StateRow> ReadLiveStateAsync(Guid journeyId)
    {
        var stops = await _postgres.QueryAsync(
            $"""
            SELECT sequence, station_id, scheduled_arrival, scheduled_departure,
                   expected_arrival, expected_departure, actual_departure
            FROM operations.journey_stop_live_states
            WHERE journey_id = '{journeyId}'
            ORDER BY sequence
            """,
            reader => new StopRow(
                reader.GetInt32(0),
                reader.GetGuid(1),
                reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2),
                reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3),
                reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4),
                reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
                reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6)));

        var state = Assert.Single(await _postgres.QueryAsync(
            $"""
            SELECT lifecycle_status, delay_minutes, next_station_id
            FROM operations.journey_live_states
            WHERE journey_id = '{journeyId}'
            """,
            reader => new StateRow(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetGuid(2),
                stops)));

        return state;
    }

    /// <summary>Removes every row the test created for the journey, corrections first (self foreign key).</summary>
    private async Task DeleteJourneyAsync(Guid journeyId)
    {
        await _postgres.ExecuteAsync(
            $"DELETE FROM operations.status_changes WHERE journey_id = '{journeyId}' AND corrects_change_id IS NOT NULL");
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

    private sealed record StopRow(
        int Sequence,
        Guid StationId,
        DateTimeOffset? ScheduledArrival,
        DateTimeOffset? ScheduledDeparture,
        DateTimeOffset? ExpectedArrival,
        DateTimeOffset? ExpectedDeparture,
        DateTimeOffset? ActualDeparture);

    private sealed record StateRow(
        string Status,
        int? DelayMinutes,
        Guid? NextStationId,
        IReadOnlyList<StopRow> Stops);
}
