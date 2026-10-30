using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartRail.Audit.Contracts;
using SmartRail.Host.Http;
using SmartRail.Host.IntegrationTests.Infrastructure;
using SmartRail.RailNetwork.Contracts;
using SmartRail.RailNetwork.Infrastructure.Persistence;
using SmartRail.SharedKernel.Application.Abstractions;
using SmartRail.SharedKernel.Application.Events;

namespace SmartRail.Host.IntegrationTests.LiveOperations;

/// <summary>
/// Cancelling a journey through <c>POST /operations/journeys/{journeyId}/cancellation</c>
/// (status-publication spec, controller-operations task 2.4): the response, the journey becoming
/// <c>cancelled</c>, the rule that a journey with a recorded departure is refused, the shared today
/// and unknown-journey rules, the controller-only policy with its denied entry, the journey event and
/// outbox row that exist when the response returns, and the one audit entry with the old and new
/// status. The last case is the audit step failing: nothing may be left behind.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PublishCancellationTests
{
    /// <summary>The seeded controller account (seed/identity/staff.json).</summary>
    private static readonly Guid ControllerStaffId = Guid.Parse("01a15874-e69c-70da-a689-76a7268ed330");

    /// <summary>The seeded guard account (seed/identity/staff.json).</summary>
    private static readonly Guid GuardStaffId = Guid.Parse("01a15874-e69d-73b2-b24d-d85d22fdead2");

    /// <summary>How long a denial may take to be delivered and recorded before the test fails.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private readonly PostgresFixture _postgres;

    public PublishCancellationTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task A_valid_cancellation_returns_the_change_and_reaches_the_state_event_outbox_and_audit()
    {
        using var factory = NewFactory();
        var today = RailwayToday();
        var journeyId = await CreateTodayJourneyAsync(factory, today);
        var auditBaseline = await MaxAuditPositionAsync();

        try
        {
            using var client = factory.CreateApiClient();

            // The route needs no body, so a request with none is the plain form of the call.
            using var response = await PostCancellationAsync(client, journeyId);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var change = await response.Content.ReadFromJsonAsync<ChangeItem>();
            Assert.NotNull(change);

            // The response is the change itself: identifier, type, values, publisher, publish time
            // and the provenance label (status-publication spec: response content).
            Assert.NotEqual(Guid.Empty, change.Id);
            Assert.Equal("cancellation", change.Type);
            Assert.Empty(change.Values);
            Assert.Equal("Mahesh Perera", change.PublisherName);
            Assert.Equal("automatically_published", change.Provenance);
            Assert.False(change.Corrected);
            Assert.True(
                change.PublishedAt > DateTimeOffset.UtcNow.AddMinutes(-1),
                $"PublishedAt {change.PublishedAt} is not the time of the publication.");

            var stored = Assert.Single(await _postgres.QueryAsync(
                $"""
                SELECT id, change_type, stop_sequence, published_by
                FROM operations.status_changes
                WHERE journey_id = '{journeyId}'
                """,
                reader => new StoredChange(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetInt32(2),
                    reader.GetGuid(3))));

            Assert.Equal(change.Id, stored.Id);
            Assert.Equal("cancellation", stored.Type);
            Assert.Null(stored.StopSequence);
            Assert.Equal(ControllerStaffId, stored.Publisher);

            // A cancelled journey reports no expected times and no next station, and every stop
            // keeps its scheduled values.
            var detail = await GetDetailAsync(client, journeyId);

            Assert.Equal("cancelled", detail.Journey.Status);
            Assert.Null(detail.Journey.NextStation);
            Assert.All(detail.Stops, stop =>
            {
                Assert.Null(stop.ExpectedArrival);
                Assert.Null(stop.ExpectedDeparture);
            });

            // Published without review: the journey event and its outbox row exist by the time the
            // response returns, and the event names the change as its source.
            var journeyEvent = Assert.Single(await _postgres.QueryAsync(
                $"""
                SELECT event_type, sequence_number, status_change_id
                FROM operations.journey_events
                WHERE journey_id = '{journeyId}'
                ORDER BY sequence_number
                """,
                reader => new JourneyEventRow(
                    reader.GetString(0),
                    reader.GetInt64(1),
                    reader.GetGuid(2))));

            Assert.Equal("cancellation_published", journeyEvent.Type);
            Assert.Equal(1, journeyEvent.SequenceNumber);
            Assert.Equal(change.Id, journeyEvent.SourceId);

            Assert.Equal("JourneyStateChanged", Assert.Single(await _postgres.QueryAsync(
                $"SELECT event_type FROM operations.outbox_messages WHERE ordering_key = '{journeyId}'",
                reader => reader.GetString(0))));

            // Exactly one audit entry: the controller, the role, the action, the journey, and the
            // status before and after the cancellation (M-03, AUD-01, design D4).
            var entry = Assert.Single(await _postgres.QueryAsync(
                $"""
                SELECT actor_staff_id, actor_role, action, target_type, target_id, outcome,
                       before::text, after::text
                FROM audit.entries
                WHERE target_id = '{journeyId}'
                """,
                reader => new AuditRow(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7))));

            Assert.Equal(ControllerStaffId, entry.Actor);
            Assert.Equal(Roles.NetworkController, entry.Role);
            Assert.Equal("PublishCancellation", entry.Action);
            Assert.Equal("journey", entry.TargetType);
            Assert.Equal("succeeded", entry.Outcome);

            using var before = JsonDocument.Parse(entry.Before!);
            Assert.Equal("scheduled", before.RootElement.GetProperty("status").GetString());

            using var after = JsonDocument.Parse(entry.After!);
            Assert.Equal("cancelled", after.RootElement.GetProperty("status").GetString());
        }
        finally
        {
            await CleanUpJourneyAsync(journeyId);
            await ResetAuditEntriesAsync(auditBaseline);
        }
    }

    [Fact]
    public async Task An_empty_json_body_is_accepted_like_no_body()
    {
        using var factory = NewFactory();
        var journeyId = await CreateTodayJourneyAsync(factory, RailwayToday());
        var auditBaseline = await MaxAuditPositionAsync();

        try
        {
            using var client = factory.CreateApiClient();
            using var response = await PostCancellationAsync(client, journeyId, new { });

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }
        finally
        {
            await CleanUpJourneyAsync(journeyId);
            await ResetAuditEntriesAsync(auditBaseline);
        }
    }

    [Fact]
    public async Task A_journey_with_a_recorded_departure_is_a_conflict_and_changes_nothing()
    {
        using var factory = NewFactory();
        var today = RailwayToday();
        var journeyId = await CreateTodayJourneyAsync(factory, today);
        var auditBaseline = await MaxAuditPositionAsync();

        try
        {
            // The Guard has departed stop 1, so the journey is running and must be ended as an
            // early termination instead.
            await _postgres.ExecuteAsync(
                $"""
                UPDATE operations.journey_stop_live_states
                SET actual_departure = '{DepartAt(today):O}'
                WHERE journey_id = '{journeyId}' AND sequence = 1
                """);
            await _postgres.ExecuteAsync(
                $"UPDATE operations.journey_live_states SET lifecycle_status = 'running' WHERE journey_id = '{journeyId}'");

            using var client = factory.CreateApiClient();
            using var response = await PostCancellationAsync(client, journeyId);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

            var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Contains(
                "early termination",
                problem.RootElement.GetProperty("detail").GetString()!);

            Assert.Empty(await _postgres.QueryAsync(
                $"SELECT id FROM operations.status_changes WHERE journey_id = '{journeyId}'",
                reader => reader.GetGuid(0)));
            Assert.Empty(await _postgres.QueryAsync(
                $"SELECT id FROM operations.journey_events WHERE journey_id = '{journeyId}'",
                reader => reader.GetGuid(0)));
        }
        finally
        {
            await CleanUpJourneyAsync(journeyId);
            await ResetAuditEntriesAsync(auditBaseline);
        }
    }

    [Fact]
    public async Task An_already_cancelled_journey_is_a_conflict()
    {
        using var factory = NewFactory();
        var journeyId = await CreateTodayJourneyAsync(factory, RailwayToday());
        var auditBaseline = await MaxAuditPositionAsync();

        try
        {
            using var client = factory.CreateApiClient();

            using var first = await PostCancellationAsync(client, journeyId);
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);

            using var second = await PostCancellationAsync(client, journeyId);

            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
            Assert.Equal(1, await CountAsync(
                $"SELECT count(*) FROM operations.status_changes WHERE journey_id = '{journeyId}'"));
            Assert.Equal(1, await CountAsync(
                $"SELECT count(*) FROM operations.journey_events WHERE journey_id = '{journeyId}'"));
        }
        finally
        {
            await CleanUpJourneyAsync(journeyId);
            await ResetAuditEntriesAsync(auditBaseline);
        }
    }

    [Fact]
    public async Task A_cancellation_on_a_journey_dated_tomorrow_is_a_bad_request()
    {
        using var factory = NewFactory();
        var tomorrow = RailwayToday().AddDays(1);
        var journeyId = await CreateJourneyAsync(factory, tomorrow, CallingStops(tomorrow));

        try
        {
            using var client = factory.CreateApiClient();
            using var response = await PostCancellationAsync(client, journeyId);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Empty(await _postgres.QueryAsync(
                $"SELECT id FROM operations.status_changes WHERE journey_id = '{journeyId}'",
                reader => reader.GetGuid(0)));
        }
        finally
        {
            await CleanUpJourneyAsync(journeyId);
        }
    }

    [Fact]
    public async Task A_cancellation_on_an_unknown_journey_is_not_found()
    {
        using var factory = NewFactory();
        await StartAsync(factory);

        using var client = factory.CreateApiClient();
        using var response = await PostCancellationAsync(client, Guid.CreateVersion7());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_request_with_no_token_is_unauthorized()
    {
        using var factory = NewFactory();
        var journeyId = await CreateTodayJourneyAsync(factory, RailwayToday());

        try
        {
            using var client = factory.CreateApiClient();
            using var response = await client.SendAsync(
                new HttpRequestMessage(HttpMethod.Post, CancellationRoute(journeyId)));

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        finally
        {
            await CleanUpJourneyAsync(journeyId);
        }
    }

    [Theory]
    [InlineData("guard.ishara", Roles.Guard, "01a15874-e69d-73b2-b24d-d85d22fdead2")]
    [InlineData("auditor.wasantha", Roles.Auditor, "01a15874-e6a0-7fd3-8c46-34bd36e8f16d")]
    public async Task Only_the_network_controller_may_cancel_and_the_denial_is_audited(
        string subject,
        string role,
        string staffId)
    {
        using var factory = NewFactory();
        var journeyId = await CreateTodayJourneyAsync(factory, RailwayToday());
        var auditBaseline = await MaxAuditPositionAsync();
        var outboxBaseline = await MaxIdentityOutboxPositionAsync();
        Guid? denialEventId = null;

        try
        {
            using var client = factory.CreateApiClient();
            using var response = await PostCancellationAsync(
                client,
                journeyId,
                token: TestTokens.Create(subject, role));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Empty(await _postgres.QueryAsync(
                $"SELECT id FROM operations.status_changes WHERE journey_id = '{journeyId}'",
                reader => reader.GetGuid(0)));

            denialEventId = await NewestDenialMessageAsync(outboxBaseline);

            // A denied write is recorded shortly after the refusal, by the bus (audit D5). Waiting
            // for it also means the cleanup below cannot race a late delivery.
            var denial = await WaitForDenialAsync(Guid.Parse(staffId));

            Assert.Equal(Guid.Parse(staffId), denial.Actor);
            Assert.Equal(role, denial.Role);
            Assert.Equal("PublishCancellation", denial.Action);
            Assert.NotNull(denialEventId);
            Assert.Equal(denialEventId.Value, denial.SourceEventId);
        }
        finally
        {
            await CleanUpJourneyAsync(journeyId);
            await ResetAuditEntriesAsync(auditBaseline);

            if (denialEventId is { } id)
            {
                await _postgres.ExecuteAsync(
                    $"DELETE FROM identity.outbox_deliveries WHERE message_id = '{id}'");
                await _postgres.ExecuteAsync($"DELETE FROM identity.outbox_messages WHERE id = '{id}'");
            }
        }
    }

    /// <summary>
    /// The audit entry commits with the cancellation or the cancellation does not happen. The
    /// recorder throws after the change row, the journey event and the outbox row were written, so
    /// the shared request transaction has to roll all three back.
    /// </summary>
    [Fact]
    public async Task A_failing_audit_step_leaves_no_change_event_outbox_row_or_state_change()
    {
        using var factory = NewFactory(services =>
        {
            services.RemoveAll<IAuditRecorder>();
            services.AddScoped<IAuditRecorder, ThrowingAuditRecorder>();
        });

        var journeyId = await CreateTodayJourneyAsync(factory, RailwayToday());
        var stateBefore = await ReadLiveStateAsync(journeyId);

        try
        {
            using var client = factory.CreateApiClient();
            using var response = await PostCancellationAsync(client, journeyId);

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

            Assert.Equal(0, await CountAsync(
                $"SELECT count(*) FROM operations.status_changes WHERE journey_id = '{journeyId}'"));
            Assert.Equal(0, await CountAsync(
                $"SELECT count(*) FROM operations.journey_events WHERE journey_id = '{journeyId}'"));
            Assert.Equal(0, await CountAsync(
                $"SELECT count(*) FROM operations.outbox_messages WHERE ordering_key = '{journeyId}'"));

            Assert.Equal(stateBefore, await ReadLiveStateAsync(journeyId));
        }
        finally
        {
            await CleanUpJourneyAsync(journeyId);
        }
    }

    private SmartRailApiFactory NewFactory(Action<IServiceCollection>? configureServices = null) =>
        new(_postgres.ConnectionString, configureServices: configureServices);

    private static DateOnly RailwayToday() => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTime(
            DateTimeOffset.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Colombo")).DateTime);

    private static DateTimeOffset DepartAt(DateOnly serviceDate) =>
        new(serviceDate.ToDateTime(new TimeOnly(2, 0)), TimeSpan.Zero);

    private static string CancellationRoute(Guid journeyId) =>
        $"{ApiRoutes.BasePath}/operations/journeys/{journeyId}/cancellation";

    private static string DetailRoute(Guid journeyId) =>
        $"{ApiRoutes.BasePath}/operations/journeys/{journeyId}";

    /// <summary>Three calling stops, the shape the shared rules are checked against.</summary>
    private static IReadOnlyList<JourneyStopDto> CallingStops(DateOnly serviceDate)
    {
        var departAt = DepartAt(serviceDate);

        return
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
        ];
    }

    private async Task<Guid> CreateTodayJourneyAsync(SmartRailApiFactory factory, DateOnly serviceDate) =>
        await CreateJourneyAsync(factory, serviceDate, CallingStops(serviceDate));

    private async Task<Guid> CreateJourneyAsync(
        SmartRailApiFactory factory,
        DateOnly serviceDate,
        IReadOnlyList<JourneyStopDto> stops)
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
                serviceDate,
                Guid.CreateVersion7(),
                ServiceDirection.Outbound,
                GuardStaffId,
                stops),
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

    private static async Task StartAsync(SmartRailApiFactory factory)
    {
        using var client = factory.CreateApiClient();
        using var response = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> PostCancellationAsync(
        HttpClient client,
        Guid journeyId,
        object? body = null,
        string? token = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, CancellationRoute(journeyId));

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        request.WithStaffToken(token ?? TestTokens.Create("controller.mahesh", Roles.NetworkController));

        return await client.SendAsync(request);
    }

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

    private async Task<int> CountAsync(string sql)
    {
        var count = Assert.Single(await _postgres.QueryAsync(sql, reader => reader.GetInt64(0)));
        return (int)count;
    }

    /// <summary>The journey's stored state, so a refused change can be shown to have left it alone.</summary>
    private async Task<string> ReadLiveStateAsync(Guid journeyId)
    {
        var stops = await _postgres.QueryAsync(
            $"""
            SELECT sequence, expected_arrival, expected_departure, actual_departure, platform_id
            FROM operations.journey_stop_live_states
            WHERE journey_id = '{journeyId}'
            ORDER BY sequence
            """,
            reader => string.Join(
                '|',
                reader.GetInt32(0),
                reader.IsDBNull(1) ? "-" : reader.GetFieldValue<DateTimeOffset>(1).ToString("O"),
                reader.IsDBNull(2) ? "-" : reader.GetFieldValue<DateTimeOffset>(2).ToString("O"),
                reader.IsDBNull(3) ? "-" : reader.GetFieldValue<DateTimeOffset>(3).ToString("O"),
                reader.IsDBNull(4) ? "-" : reader.GetGuid(4).ToString()));

        var state = Assert.Single(await _postgres.QueryAsync(
            $"""
            SELECT lifecycle_status, delay_minutes, last_departed_station_id, next_station_id,
                   last_sequence_number
            FROM operations.journey_live_states
            WHERE journey_id = '{journeyId}'
            """,
            reader => string.Join(
                '|',
                reader.GetString(0),
                reader.IsDBNull(1) ? "-" : reader.GetInt32(1).ToString(),
                reader.IsDBNull(2) ? "-" : reader.GetGuid(2).ToString(),
                reader.IsDBNull(3) ? "-" : reader.GetGuid(3).ToString(),
                reader.GetInt64(4))));

        return $"{state}::{string.Join(';', stops)}";
    }

    private async Task<long> MaxAuditPositionAsync() =>
        Assert.Single(await _postgres.QueryAsync(
            "SELECT coalesce(max(chain_position), 0) FROM audit.entries",
            reader => reader.GetInt64(0)));

    private async Task<long> MaxIdentityOutboxPositionAsync() =>
        Assert.Single(await _postgres.QueryAsync(
            "SELECT coalesce(max(position), 0) FROM identity.outbox_messages",
            reader => reader.GetInt64(0)));

    /// <summary>The denial message this request published, so the test can remove it again.</summary>
    private async Task<Guid?> NewestDenialMessageAsync(long baselinePosition)
    {
        var ids = await _postgres.QueryAsync(
            $"""
            SELECT id
            FROM identity.outbox_messages
            WHERE event_type = 'AccessDenied' AND position > {baselinePosition}
            ORDER BY position DESC
            LIMIT 1
            """,
            reader => reader.GetGuid(0));

        return ids.Count > 0 ? ids[0] : null;
    }

    /// <summary>
    /// Waits for a refused write to reach the audit record. Delivery is asynchronous, so the entry
    /// appears shortly after the 403 (audit design D5).
    /// </summary>
    private async Task<DenialRow> WaitForDenialAsync(Guid staffId)
    {
        var deadline = DateTimeOffset.UtcNow.Add(Timeout);

        while (true)
        {
            var rows = await _postgres.QueryAsync(
                $"""
                SELECT actor_staff_id, actor_role, action, source_event_id
                FROM audit.entries
                WHERE actor_staff_id = '{staffId}'
                  AND outcome = 'denied'
                  AND action = 'PublishCancellation'
                ORDER BY chain_position DESC
                LIMIT 1
                """,
                reader => new DenialRow(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetGuid(3)));

            if (rows.Count > 0)
            {
                return rows[0];
            }

            Assert.True(
                DateTimeOffset.UtcNow < deadline,
                $"The refusal was not recorded within {Timeout}.");

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
    }

    /// <summary>
    /// Removes every row the test created for the journey: the change first (self reference), then
    /// the event, its outbox row, the stops and the state.
    /// </summary>
    private async Task CleanUpJourneyAsync(Guid journeyId)
    {
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

    private sealed record StoredChange(Guid Id, string Type, int? StopSequence, Guid Publisher);

    private sealed record JourneyEventRow(string Type, long SequenceNumber, Guid SourceId);

    private sealed record AuditRow(
        Guid Actor,
        string Role,
        string Action,
        string TargetType,
        string TargetId,
        string Outcome,
        string? Before,
        string? After);

    private sealed record DenialRow(Guid Actor, string Role, string Action, Guid SourceEventId);

    private sealed record StationItem(Guid Id, string Name);

    private sealed record StopItem(
        int Sequence,
        StationItem? Station,
        bool IsCalling,
        DateTimeOffset? ScheduledArrival,
        DateTimeOffset? ScheduledDeparture,
        DateTimeOffset? ExpectedArrival,
        DateTimeOffset? ExpectedDeparture,
        DateTimeOffset? ActualDeparture,
        PlatformItem? Platform);

    private sealed record PlatformItem(Guid Id, string Number);

    private sealed record JourneyItem(string? ServiceName, string Status, StationItem? NextStation);

    private sealed record JourneyDetail(
        JourneyItem Journey,
        IReadOnlyList<StopItem> Stops,
        IReadOnlyList<ChangeItem> Changes);

    private sealed record ChangeItem(
        Guid Id,
        string Type,
        IReadOnlyDictionary<string, string?> Values,
        string? PublisherName,
        DateTimeOffset PublishedAt,
        string Provenance,
        bool Corrected);

    /// <summary>An audit recorder that fails at the last step of the publishing pipeline.</summary>
    private sealed class ThrowingAuditRecorder : IAuditRecorder
    {
        public Task<Guid> RecordAsync(
            AuditRecordRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Injected audit failure after the change was written.");
    }
}
