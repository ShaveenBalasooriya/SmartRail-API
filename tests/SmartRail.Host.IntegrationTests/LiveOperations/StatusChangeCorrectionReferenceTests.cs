using System.Net;
using System.Net.Http.Json;
using MediatR;
using SmartRail.Host.Http;
using SmartRail.Host.IntegrationTests.Infrastructure;
using SmartRail.LiveOperations.Application.LiveState.ApplyJourneyFact;
using SmartRail.LiveOperations.Application.Publishing;
using SmartRail.LiveOperations.Contracts;
using SmartRail.LiveOperations.Domain.Changes;
using SmartRail.RailNetwork.Contracts;
using SmartRail.RailNetwork.Infrastructure.Persistence;
using SmartRail.SharedKernel.Application.Abstractions;
using SmartRail.SharedKernel.Application.Events;
using SmartRail.SharedKernel.Results;
using ContractJourneyEventType = SmartRail.LiveOperations.Contracts.JourneyEventType;

namespace SmartRail.Host.IntegrationTests.LiveOperations;

/// <summary>
/// The plumbing the correction use case needs (controller-operations task 3.1, design D4 and
/// package decision D-8; specs "Correction labelled, attributed and audited" and "Correction reaches
/// every channel the original reached"): every published change keeps the identifier of the audit
/// entry it produced, the correction repository can find a change and say whether it was already
/// corrected, and a <c>correction_published</c> event names the original change and the event that
/// recorded it. No correction endpoint exists yet, so the correction row is inserted as the next
/// package will insert it.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class StatusChangeCorrectionReferenceTests
{
    /// <summary>The seeded controller account (seed/identity/staff.json).</summary>
    private static readonly Guid ControllerStaffId = Guid.Parse("01a15874-e69c-70da-a689-76a7268ed330");

    /// <summary>The seeded guard account (seed/identity/staff.json).</summary>
    private static readonly Guid GuardStaffId = Guid.Parse("01a15874-e69d-73b2-b24d-d85d22fdead2");

    private readonly PostgresFixture _postgres;

    public StatusChangeCorrectionReferenceTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task The_migration_adds_a_nullable_audit_entry_id_column()
    {
        using var factory = new SmartRailApiFactory(_postgres.ConnectionString);
        await StartAsync(factory);

        var columns = await _postgres.QueryAsync(
            """
            SELECT data_type, is_nullable
            FROM information_schema.columns
            WHERE table_schema = 'operations'
              AND table_name = 'status_changes'
              AND column_name = 'audit_entry_id'
            """,
            reader => (Type: reader.GetString(0), Nullable: reader.GetString(1)));

        var column = Assert.Single(columns);
        Assert.Equal("uuid", column.Type);
        Assert.Equal("YES", column.Nullable);
    }

    [Fact]
    public async Task A_published_delay_stores_the_id_of_the_audit_entry_it_produced()
    {
        using var factory = new SmartRailApiFactory(_postgres.ConnectionString);
        var today = RailwayToday();
        var journeyId = await CreateTodayJourneyAsync(factory, today);
        var auditBaseline = await MaxAuditPositionAsync();

        try
        {
            using var client = factory.CreateApiClient();
            using var response = await PostDelayAsync(client, journeyId, fromStopSequence: 2, minutes: 15);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var change = await response.Content.ReadFromJsonAsync<ChangeItem>();
            Assert.NotNull(change);

            var auditEntryId = Assert.Single(await _postgres.QueryAsync(
                $"""
                SELECT audit_entry_id
                FROM operations.status_changes
                WHERE journey_id = '{journeyId}'
                """,
                reader => reader.IsDBNull(0) ? (Guid?)null : reader.GetGuid(0)));

            Assert.NotNull(auditEntryId);

            // The stored ID is the entry this action appended, and that entry still targets the
            // journey: the change cross-references it without searching the audit record (D-8).
            var entry = Assert.Single(await _postgres.QueryAsync(
                $"""
                SELECT id, actor_staff_id, action, target_type, target_id
                FROM audit.entries
                WHERE chain_position > {auditBaseline}
                  AND outcome = 'succeeded'
                  AND action = 'PublishDelay'
                """,
                reader => new AuditRow(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4))));

            Assert.Equal(entry.Id, auditEntryId!.Value);
            Assert.Equal(ControllerStaffId, entry.Actor);
            Assert.Equal("journey", entry.TargetType);
            Assert.Equal(journeyId.ToString(), entry.TargetId);
        }
        finally
        {
            await CleanUpJourneyAsync(journeyId);
            await ResetAuditEntriesAsync(auditBaseline);
        }
    }

    [Fact]
    public async Task The_change_repository_reads_a_change_and_says_whether_it_was_corrected()
    {
        using var factory = new SmartRailApiFactory(_postgres.ConnectionString);
        var today = RailwayToday();
        var journeyId = await CreateTodayJourneyAsync(factory, today);

        try
        {
            var publishedAt = DepartAt(today).AddMinutes(-5);
            var delayId = Guid.CreateVersion7();
            var cancellationId = Guid.CreateVersion7();
            var correctionId = Guid.CreateVersion7();

            await InsertDelayAsync(journeyId, delayId, stopSequence: 2, minutes: 15, publishedAt);
            await InsertCancellationAsync(journeyId, cancellationId, publishedAt);
            await InsertCorrectionAsync(journeyId, correctionId, cancellationId, publishedAt.AddMinutes(1));

            using var scope = factory.Services.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IStatusChangeRepository>();

            var corrected = await repository.GetAsync(cancellationId);
            Assert.NotNull(corrected);
            Assert.Equal(StatusChangeType.Cancellation, corrected.Type);
            Assert.True(await repository.IsCorrectedAsync(cancellationId));

            // The read is tracked, so the correction pipeline can act on the original in the same
            // transaction: the second read is the same instance.
            Assert.Same(corrected, await repository.GetAsync(cancellationId));

            var correction = await repository.GetAsync(correctionId);
            Assert.NotNull(correction);
            Assert.Equal(StatusChangeType.Correction, correction.Type);
            Assert.Equal(cancellationId, correction.CorrectsChangeId);

            // A correction cannot itself be corrected (change-correction spec).
            Assert.False(await repository.IsCorrectedAsync(correctionId));

            Assert.False(await repository.IsCorrectedAsync(delayId));
            Assert.Null(await repository.GetAsync(Guid.CreateVersion7()));
        }
        finally
        {
            await CleanUpJourneyAsync(journeyId);
        }
    }

    [Fact]
    public async Task A_correction_event_names_the_original_change_and_its_event()
    {
        using var factory = new SmartRailApiFactory(_postgres.ConnectionString);
        var today = RailwayToday();
        var journeyId = await CreateTodayJourneyAsync(factory, today);
        var auditBaseline = await MaxAuditPositionAsync();

        try
        {
            using var client = factory.CreateApiClient();
            using var response = await PostDelayAsync(client, journeyId, fromStopSequence: 2, minutes: 15);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var original = await response.Content.ReadFromJsonAsync<ChangeItem>();
            Assert.NotNull(original);

            var originalEvent = Assert.Single(await _postgres.QueryAsync(
                $"""
                SELECT id, event_type, status_change_id
                FROM operations.journey_events
                WHERE journey_id = '{journeyId}'
                """,
                reader => new JourneyEventRow(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetGuid(2))));

            Assert.Equal("delay_published", originalEvent.Type);
            Assert.Equal(original.Id, originalEvent.SourceId);

            // The correction row the next package will insert: it withdraws the original delay.
            var correctionId = Guid.CreateVersion7();
            var correctedAt = DateTimeOffset.UtcNow;
            await InsertCorrectionAsync(journeyId, correctionId, original.Id, correctedAt);

            var applied = await SendAsync(factory, new ApplyJourneyFactCommand(
                journeyId,
                Fact: null,
                ContractJourneyEventType.CorrectionPublished,
                correctionId,
                correctedAt,
                correctedAt));
            Assert.True(applied.IsSuccess);

            using var scope = factory.Services.CreateScope();
            var events = await scope.ServiceProvider
                .GetRequiredService<ILiveJourneys>()
                .GetEventsAfterAsync(0, journeyId);

            Assert.Equal(2, events.Count);

            // Events of other types carry no correction reference.
            var delayEvent = Assert.Single(events, journeyEvent => journeyEvent.SourceId == original.Id);
            Assert.Equal(ContractJourneyEventType.DelayPublished, delayEvent.Type);
            Assert.Null(delayEvent.CorrectsChangeId);
            Assert.Null(delayEvent.CorrectsEventId);

            // The correction names the original change and the event that recorded it.
            var correctionEvent = Assert.Single(events, journeyEvent => journeyEvent.SourceId == correctionId);
            Assert.Equal(ContractJourneyEventType.CorrectionPublished, correctionEvent.Type);
            Assert.Equal(original.Id, correctionEvent.CorrectsChangeId);
            Assert.Equal(originalEvent.Id, correctionEvent.CorrectsEventId);
        }
        finally
        {
            await CleanUpJourneyAsync(journeyId);
            await ResetAuditEntriesAsync(auditBaseline);
        }
    }

    private static DateOnly RailwayToday() => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTime(
            DateTimeOffset.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Colombo")).DateTime);

    private static DateTimeOffset DepartAt(DateOnly serviceDate) =>
        new(serviceDate.ToDateTime(new TimeOnly(2, 0)), TimeSpan.Zero);

    private static string DelayRoute(Guid journeyId) =>
        $"{ApiRoutes.BasePath}/operations/journeys/{journeyId}/delays";

    private static async Task StartAsync(SmartRailApiFactory factory)
    {
        using var client = factory.CreateApiClient();
        using var response = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> PostDelayAsync(
        HttpClient client,
        Guid journeyId,
        int fromStopSequence,
        int minutes)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, DelayRoute(journeyId))
        {
            Content = JsonContent.Create(new { fromStopSequence, minutes }),
        };

        request.WithStaffToken(TestTokens.Create("controller.mahesh", Roles.NetworkController));

        return await client.SendAsync(request);
    }

    /// <summary>Creates the journey's live state through the real generator announcement.</summary>
    private async Task<Guid> CreateTodayJourneyAsync(SmartRailApiFactory factory, DateOnly serviceDate)
    {
        await StartAsync(factory);

        var journeyId = Guid.CreateVersion7();
        var departAt = DepartAt(serviceDate);

        using var scope = factory.Services.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredKeyedService<IIntegrationEventPublisher>(
            IntegrationEventPublisherKeys.Network);
        var database = scope.ServiceProvider.GetRequiredService<RailNetworkDbContext>();

        publisher.Publish(
            new JourneyGenerated(
                journeyId,
                Guid.CreateVersion7(),
                serviceDate,
                Guid.CreateVersion7(),
                ServiceDirection.Outbound,
                GuardStaffId,
                [
                    new JourneyStopDto(1, Guid.CreateVersion7(), true, null, departAt, Guid.CreateVersion7()),
                    new JourneyStopDto(
                        2,
                        Guid.CreateVersion7(),
                        true,
                        departAt.AddMinutes(10),
                        departAt.AddMinutes(11),
                        Guid.CreateVersion7()),
                    new JourneyStopDto(3, Guid.CreateVersion7(), true, departAt.AddMinutes(20), null, null),
                ]),
            journeyId);

        await database.SaveChangesAsync();

        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (await CountAsync(
                   $"SELECT count(*) FROM operations.journey_live_states WHERE journey_id = '{journeyId}'") == 0
               && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        Assert.Equal(1, await CountAsync(
            $"SELECT count(*) FROM operations.journey_live_states WHERE journey_id = '{journeyId}'"));

        return journeyId;
    }

    private static async Task<Result> SendAsync(
        SmartRailApiFactory factory,
        ApplyJourneyFactCommand command)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(command);
    }

    private async Task<int> CountAsync(string sql)
    {
        var count = Assert.Single(await _postgres.QueryAsync(sql, reader => reader.GetInt64(0)));
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
        _postgres.ExecuteAsync(
            $"""
            INSERT INTO operations.status_changes
                (id, journey_id, change_type, corrects_change_id, provenance, published_by, published_at)
            VALUES
                ('{changeId}', '{journeyId}', 'correction', '{correctsChangeId}',
                 'automatically_published', '{ControllerStaffId}', '{publishedAt:O}')
            """);

    private async Task<long> MaxAuditPositionAsync() =>
        Assert.Single(await _postgres.QueryAsync(
            "SELECT coalesce(max(chain_position), 0) FROM audit.entries",
            reader => reader.GetInt64(0)));

    /// <summary>
    /// Removes every row the test created for the journey, corrections first (self foreign key),
    /// then the event, its outbox row, the stops and the state.
    /// </summary>
    private async Task CleanUpJourneyAsync(Guid journeyId)
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

    /// <summary>
    /// Removes the entries this test appended, leaving the earlier chain intact. The table is
    /// append-only, so the trigger is disabled for the delete and enabled again straight after.
    /// </summary>
    private async Task ResetAuditEntriesAsync(long baselinePosition)
    {
        await _postgres.ExecuteAsync("ALTER TABLE audit.entries DISABLE TRIGGER entries_append_only");

        try
        {
            await _postgres.ExecuteAsync(
                $"DELETE FROM audit.entries WHERE chain_position > {baselinePosition}");
        }
        finally
        {
            await _postgres.ExecuteAsync("ALTER TABLE audit.entries ENABLE TRIGGER entries_append_only");
        }
    }

    private sealed record JourneyEventRow(Guid Id, string Type, Guid SourceId);

    private sealed record AuditRow(Guid Id, Guid Actor, string Action, string TargetType, string TargetId);

    private sealed record ChangeItem(
        Guid Id,
        string Type,
        IReadOnlyDictionary<string, string?> Values,
        string? PublisherName,
        DateTimeOffset PublishedAt,
        string Provenance,
        bool Corrected);
}
