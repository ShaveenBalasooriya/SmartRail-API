using System.Net;
using System.Net.Http.Json;
using MediatR;
using Npgsql;
using SmartRail.Host.Http;
using SmartRail.Host.IntegrationTests.Infrastructure;
using SmartRail.LiveOperations.Application.LiveState;
using SmartRail.LiveOperations.Application.LiveState.ApplyJourneyFact;
using SmartRail.LiveOperations.Contracts;
using SmartRail.LiveOperations.Domain.Calculation;
using SmartRail.RailNetwork.Contracts;
using SmartRail.RailNetwork.Infrastructure.Persistence;
using SmartRail.SharedKernel.Application.Abstractions;
using SmartRail.SharedKernel.Application.Events;
using SmartRail.SharedKernel.Results;

namespace SmartRail.Host.IntegrationTests.LiveOperations;

/// <summary>
/// The <c>operations.status_changes</c> table and the two readers that replace the empty ones: the
/// migration and its constraints, the commit order the calculator depends on, the corrected flag,
/// a stored delay reaching the journey's expected times, and the change appearing in the
/// controller's history (controller-operations tasks 2.1, D3, D-6).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class StatusChangeTests
{
    /// <summary>The seeded controller account (seed/identity/staff.json).</summary>
    private static readonly Guid ControllerStaffId = Guid.Parse("01a15874-e69c-70da-a689-76a7268ed330");

    /// <summary>The seeded guard account (seed/identity/staff.json).</summary>
    private static readonly Guid GuardStaffId = Guid.Parse("01a15874-e69d-73b2-b24d-d85d22fdead2");

    private readonly PostgresFixture _postgres;

    public StatusChangeTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task The_migration_creates_the_status_change_table_its_sequence_and_its_constraints()
    {
        using var factory = new SmartRailApiFactory(_postgres.ConnectionString);
        await StartAsync(factory);

        var tables = await _postgres.QueryAsync(
            "SELECT to_regclass('operations.status_changes')::text",
            reader => reader.IsDBNull(0) ? null : reader.GetString(0));
        Assert.Equal(["operations.status_changes"], tables);

        var sequences = await _postgres.QueryAsync(
            "SELECT sequencename FROM pg_sequences WHERE schemaname = 'operations' AND sequencename = 'fact_order_seq'",
            reader => reader.GetString(0));
        Assert.Equal(["fact_order_seq"], sequences);

        var defaultValue = Assert.Single(await _postgres.QueryAsync(
            """
            SELECT column_default
            FROM information_schema.columns
            WHERE table_schema = 'operations'
              AND table_name = 'status_changes'
              AND column_name = 'fact_order'
            """,
            reader => reader.GetString(0)));
        Assert.Contains("nextval", defaultValue, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fact_order_seq", defaultValue, StringComparison.Ordinal);

        var checks = await _postgres.QueryAsync(
            """
            SELECT conname
            FROM pg_constraint
            WHERE conrelid = 'operations.status_changes'::regclass
              AND contype = 'c'
            ORDER BY conname
            """,
            reader => reader.GetString(0));
        Assert.Equal(["CK_status_changes_change_type", "CK_status_changes_provenance"], checks);

        var foreignKeys = await _postgres.QueryAsync(
            """
            SELECT conname
            FROM pg_constraint
            WHERE conrelid = 'operations.status_changes'::regclass
              AND contype = 'f'
            ORDER BY conname
            """,
            reader => reader.GetString(0));
        Assert.Equal(
            [
                "FK_status_changes_journey_live_states_journey_id",
                "FK_status_changes_status_changes_corrects_change_id",
            ],
            foreignKeys);

        var uniqueIndex = Assert.Single(await _postgres.QueryAsync(
            """
            SELECT indexdef
            FROM pg_indexes
            WHERE schemaname = 'operations'
              AND tablename = 'status_changes'
              AND indexname = 'IX_status_changes_corrects_change_id'
            """,
            reader => reader.GetString(0)));
        Assert.StartsWith("CREATE UNIQUE INDEX", uniqueIndex, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_fact_reader_orders_by_commit_and_flags_a_corrected_change()
    {
        using var factory = new SmartRailApiFactory(_postgres.ConnectionString);
        await StartAsync(factory);

        var journeyId = await CreateLiveJourneyAsync(factory);

        try
        {
            var publishedAt = DepartAt.AddMinutes(-5);
            var delayId = Guid.CreateVersion7();
            await InsertDelayAsync(journeyId, delayId, stopSequence: 2, minutes: 15, publishedAt);

            var cancellationId = Guid.CreateVersion7();
            await InsertCancellationAsync(journeyId, cancellationId, publishedAt.AddMinutes(1));

            var correctionId = Guid.CreateVersion7();
            await InsertCorrectionAsync(journeyId, correctionId, delayId, publishedAt.AddMinutes(2));

            var facts = await ReadFactsAsync(factory, journeyId);

            var delay = Assert.IsType<ChangeFact>(facts[0]);
            Assert.Equal(ChangeKind.Delay, delay.Kind);
            Assert.Equal(2, delay.StopSequence);
            Assert.Equal(15, delay.DelayMinutes);
            Assert.True(delay.Corrected);
            Assert.True(delay.StreamOrder > 0);

            var cancellation = Assert.IsType<ChangeFact>(facts[1]);
            Assert.Equal(ChangeKind.Cancellation, cancellation.Kind);
            Assert.False(cancellation.Corrected);
            Assert.True(cancellation.StreamOrder > delay.StreamOrder);

            // A correction withdraws its target and contributes no fact of its own.
            Assert.Equal(2, facts.Count);
        }
        finally
        {
            await DeleteJourneyAsync(journeyId);
        }
    }

    [Fact]
    public async Task A_second_correction_of_the_same_change_violates_the_unique_index()
    {
        using var factory = new SmartRailApiFactory(_postgres.ConnectionString);
        await StartAsync(factory);

        var journeyId = await CreateLiveJourneyAsync(factory);

        try
        {
            var publishedAt = DepartAt.AddMinutes(-5);
            var cancellationId = Guid.CreateVersion7();
            await InsertCancellationAsync(journeyId, cancellationId, publishedAt);
            await InsertCorrectionAsync(journeyId, Guid.CreateVersion7(), cancellationId, publishedAt.AddMinutes(1));

            var exception = await Assert.ThrowsAsync<PostgresException>(() => _postgres.ExecuteAsync(
                CorrectionSql(journeyId, Guid.CreateVersion7(), cancellationId, publishedAt.AddMinutes(2))));

            Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
        }
        finally
        {
            await DeleteJourneyAsync(journeyId);
        }
    }

    [Fact]
    public async Task Applying_a_stored_delay_fact_gives_the_journey_its_expected_times()
    {
        using var factory = new SmartRailApiFactory(_postgres.ConnectionString);
        await StartAsync(factory);

        var journeyId = await CreateLiveJourneyAsync(factory);

        try
        {
            var publishedAt = DepartAt.AddMinutes(-5);
            var delayId = Guid.CreateVersion7();
            await InsertDelayAsync(journeyId, delayId, stopSequence: 2, minutes: 15, publishedAt);

            var factOrder = Assert.Single(await _postgres.QueryAsync(
                $"SELECT fact_order FROM operations.status_changes WHERE id = '{delayId}'",
                reader => reader.GetInt64(0)));

            var fact = new ChangeFact(
                Kind: ChangeKind.Delay,
                StopSequence: 2,
                DelayMinutes: 15,
                PlatformId: null,
                PublishedAt: publishedAt,
                StreamOrder: factOrder,
                Corrected: false);

            var result = await SendAsync(factory, new ApplyJourneyFactCommand(
                journeyId,
                fact,
                JourneyEventType.DelayPublished,
                delayId,
                publishedAt,
                publishedAt));
            Assert.True(result.IsSuccess);

            var stops = await _postgres.QueryAsync(
                $"""
                SELECT expected_arrival, expected_departure
                FROM operations.journey_stop_live_states
                WHERE journey_id = '{journeyId}'
                ORDER BY sequence
                """,
                reader => new ExpectedStop(
                    reader.IsDBNull(0) ? null : reader.GetFieldValue<DateTimeOffset>(0),
                    reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1)));

            Assert.Equal(3, stops.Count);
            Assert.Null(stops[0].ExpectedArrival);
            Assert.Equal(DepartAt, stops[0].ExpectedDeparture);
            Assert.Equal(DepartAt.AddMinutes(10).AddMinutes(15), stops[1].ExpectedArrival);
            Assert.Equal(DepartAt.AddMinutes(11).AddMinutes(15), stops[1].ExpectedDeparture);
            Assert.Equal(DepartAt.AddMinutes(20).AddMinutes(15), stops[2].ExpectedArrival);
            Assert.Null(stops[2].ExpectedDeparture);

            var eventTypes = await _postgres.QueryAsync(
                $"SELECT event_type FROM operations.journey_events WHERE journey_id = '{journeyId}'",
                reader => reader.GetString(0));
            Assert.Equal(["delay_published"], eventTypes);
        }
        finally
        {
            await DeleteJourneyAsync(journeyId);
        }
    }

    [Fact]
    public async Task The_controller_sees_a_stored_change_and_marked_corrected_after_a_correction()
    {
        using var factory = new SmartRailApiFactory(_postgres.ConnectionString);
        await StartAsync(factory);

        var journeyId = await CreateLiveJourneyAsync(factory);

        try
        {
            var publishedAt = DepartAt.AddMinutes(-5);
            var cancellationId = Guid.CreateVersion7();
            await InsertCancellationAsync(journeyId, cancellationId, publishedAt);

            using var client = factory.CreateApiClient();
            var detail = await GetDetailAsync(client, journeyId);

            var cancellation = Assert.Single(detail.Changes);
            Assert.Equal(cancellationId, cancellation.Id);
            Assert.Equal("cancellation", cancellation.Type);
            Assert.Empty(cancellation.Values);
            Assert.Equal("Mahesh Perera", cancellation.PublisherName);
            Assert.Equal("automatically_published", cancellation.Provenance);
            Assert.False(cancellation.Corrected);

            var correctionId = Guid.CreateVersion7();
            await InsertCorrectionAsync(journeyId, correctionId, cancellationId, publishedAt.AddMinutes(1));

            detail = await GetDetailAsync(client, journeyId);

            Assert.Equal(2, detail.Changes.Count);
            Assert.True(detail.Changes[0].Corrected);

            var correction = detail.Changes[1];
            Assert.Equal(correctionId, correction.Id);
            Assert.Equal("correction", correction.Type);
            Assert.Equal(cancellationId.ToString(), correction.Values["correctsChangeId"]);
            Assert.False(correction.Corrected);
        }
        finally
        {
            await DeleteJourneyAsync(journeyId);
        }
    }

    private static readonly DateTimeOffset DepartAt = new(2026, 10, 28, 8, 0, 0, TimeSpan.Zero);

    private static string DetailRoute(Guid journeyId) =>
        $"{ApiRoutes.BasePath}/operations/journeys/{journeyId}";

    private static async Task<JourneyDetail> GetDetailAsync(HttpClient client, Guid journeyId)
    {
        using var response = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, DetailRoute(journeyId))
                .WithStaffToken(TestTokens.Create("controller.mahesh", Roles.NetworkController)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await response.Content.ReadFromJsonAsync<JourneyDetail>();
        Assert.NotNull(detail);
        return detail;
    }

    private static async Task StartAsync(SmartRailApiFactory factory)
    {
        using var client = factory.CreateApiClient();
        var ready = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
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

    private async Task<int> CountAsync(Guid journeyId)
    {
        var count = Assert.Single(await _postgres.QueryAsync(
            $"SELECT count(*) FROM operations.journey_live_states WHERE journey_id = '{journeyId}'",
            reader => reader.GetInt64(0)));

        return (int)count;
    }

    private Task<int> InsertDelayAsync(
        Guid journeyId,
        Guid changeId,
        int stopSequence,
        int minutes,
        DateTimeOffset publishedAt) =>
        _postgres.ExecuteAsync(
            $"""
            INSERT INTO operations.status_changes
                (id, journey_id, change_type, stop_sequence, delay_minutes, provenance, published_by, published_at)
            VALUES
                ('{changeId}', '{journeyId}', 'delay', {stopSequence}, {minutes},
                 'automatically_published', '{ControllerStaffId}', '{publishedAt:O}')
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

    private Task<int> InsertCorrectionAsync(
        Guid journeyId,
        Guid changeId,
        Guid correctsChangeId,
        DateTimeOffset publishedAt) =>
        _postgres.ExecuteAsync(CorrectionSql(journeyId, changeId, correctsChangeId, publishedAt));

    private static string CorrectionSql(
        Guid journeyId,
        Guid changeId,
        Guid correctsChangeId,
        DateTimeOffset publishedAt) =>
        $"""
        INSERT INTO operations.status_changes
            (id, journey_id, change_type, corrects_change_id, provenance, published_by, published_at)
        VALUES
            ('{changeId}', '{journeyId}', 'correction', '{correctsChangeId}',
             'automatically_published', '{ControllerStaffId}', '{publishedAt:O}')
        """;

    /// <summary>Removes every row the test created for the journey, corrections first (self foreign key).</summary>
    private async Task DeleteJourneyAsync(Guid journeyId)
    {
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

    private sealed record ExpectedStop(
        DateTimeOffset? ExpectedArrival,
        DateTimeOffset? ExpectedDeparture);

    private sealed record ChangeItem(
        Guid Id,
        string Type,
        IReadOnlyDictionary<string, string?> Values,
        string? PublisherName,
        DateTimeOffset PublishedAt,
        string Provenance,
        bool Corrected);

    private sealed record JourneyDetail(
        object Journey,
        IReadOnlyList<object> Stops,
        IReadOnlyList<ChangeItem> Changes);
}
