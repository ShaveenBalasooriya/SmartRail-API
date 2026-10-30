using System.Net;
using System.Net.Http.Json;
using SmartRail.Host.IntegrationTests.Infrastructure;
using SmartRail.RailNetwork.Contracts;
using SmartRail.RailNetwork.Infrastructure.Persistence;
using SmartRail.SharedKernel.Application.Events;

namespace SmartRail.Host.IntegrationTests.LiveOperations;

/// <summary>
/// The one risky point of applying a journey fact inside a request (controller-operations design D2,
/// audit design D1): the handler joins the request's transaction instead of opening its own, so its
/// row lock is taken on the request's connection and the journey event, its outbox row and the audit
/// entry commit or roll back as one unit. Two requests for the same journey still serialise on the
/// row lock, and a handler called outside a request is unchanged.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class JourneyFactRequestTransactionTests : IDisposable
{
    private static readonly DateTimeOffset DepartAt = new(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);

    private readonly PostgresFixture _postgres;
    private readonly SmartRailApiFactory _factory;
    private readonly HttpClient _client;

    public JourneyFactRequestTransactionTests(PostgresFixture postgres)
    {
        _postgres = postgres;
        _factory = new SmartRailApiFactory(postgres.ConnectionString);
        _client = _factory.CreateApiClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task A_request_that_applies_a_fact_commits_the_event_its_outbox_row_and_the_audit_entry()
    {
        var journeyId = Guid.CreateVersion7();
        var staffId = Guid.CreateVersion7();

        try
        {
            await CreateLiveJourneyAsync(journeyId);

            using var response = await PostFactAsync(journeyId, 1, DepartAt.AddMinutes(3), staffId, mode: null);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JourneyFactResponse>();
            Assert.NotNull(body);

            var journeyEventId = Assert.Single(await _postgres.QueryAsync(
                $"SELECT id FROM operations.journey_events WHERE journey_id = '{journeyId}'",
                reader => reader.GetGuid(0)));

            var messageId = Assert.Single(await _postgres.QueryAsync(
                $"SELECT id FROM operations.outbox_messages WHERE ordering_key = '{journeyId}'",
                reader => reader.GetGuid(0)));

            Assert.Equal(journeyEventId, messageId);
            Assert.Equal(1, await LastSequenceNumberAsync(journeyId));
            Assert.Equal(1, (int)await CountAuditEntriesAsync(staffId));
            Assert.True(await AuditEntryExistsAsync(body.EntryId), "The audit entry should have committed.");
        }
        finally
        {
            await CleanUpAsync(journeyId, staffId);
        }
    }

    [Theory]
    [InlineData("throw")]
    [InlineData("500")]
    public async Task A_failed_fact_request_leaves_no_event_no_outbox_row_no_audit_entry_and_the_state_unchanged(
        string mode)
    {
        var journeyId = Guid.CreateVersion7();
        var staffId = Guid.CreateVersion7();

        try
        {
            await CreateLiveJourneyAsync(journeyId);
            var before = await LiveStateAsync(journeyId);

            using var response = await PostFactAsync(journeyId, 1, DepartAt.AddMinutes(3), staffId, mode);

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Equal(0, await CountJourneyEventsAsync(journeyId));
            Assert.Equal(0, await CountOutboxMessagesAsync(journeyId));
            Assert.Equal(0, await CountAuditEntriesAsync(staffId));
            Assert.Equal(before, await LiveStateAsync(journeyId));
            Assert.Null(await LastDepartedAtAsync(journeyId, stopSequence: 1));
        }
        finally
        {
            await CleanUpAsync(journeyId, staffId);
        }
    }

    [Fact]
    public async Task Two_concurrent_fact_requests_on_one_journey_serialise_and_number_the_events_consecutively()
    {
        var journeyId = Guid.CreateVersion7();
        var firstStaffId = Guid.CreateVersion7();
        var secondStaffId = Guid.CreateVersion7();

        try
        {
            await CreateLiveJourneyAsync(journeyId);

            var responses = await Task.WhenAll(
                PostFactAsync(journeyId, 1, DepartAt.AddMinutes(3), firstStaffId, mode: null),
                PostFactAsync(journeyId, 2, DepartAt.AddMinutes(15), secondStaffId, mode: null));

            try
            {
                Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
            }
            finally
            {
                foreach (var response in responses)
                {
                    response.Dispose();
                }
            }

            var sequenceNumbers = await _postgres.QueryAsync(
                $"""
                SELECT sequence_number
                FROM operations.journey_events
                WHERE journey_id = '{journeyId}'
                ORDER BY sequence_number
                """,
                reader => reader.GetInt64(0));

            Assert.Equal(new long[] { 1, 2 }, sequenceNumbers);
            Assert.Equal(1, (int)await CountAuditEntriesAsync(firstStaffId));
            Assert.Equal(1, (int)await CountAuditEntriesAsync(secondStaffId));
        }
        finally
        {
            await CleanUpAsync(journeyId, firstStaffId, secondStaffId);
        }
    }

    /// <summary>
    /// Creates the journey's live state through the real generator announcement, as generation does:
    /// Rail Network publishes <see cref="JourneyGenerated"/> and this module's consumer creates the
    /// state on the dispatcher's poll.
    /// </summary>
    private async Task CreateLiveJourneyAsync(Guid journeyId)
    {
        using var ready = await _client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var publisher = scope.ServiceProvider.GetRequiredKeyedService<IIntegrationEventPublisher>(
                IntegrationEventPublisherKeys.Network);
            var database = scope.ServiceProvider.GetRequiredService<RailNetworkDbContext>();

            publisher.Publish(
                new JourneyGenerated(
                    journeyId,
                    Guid.CreateVersion7(),
                    new DateOnly(2026, 10, 20),
                    Guid.CreateVersion7(),
                    ServiceDirection.Outbound,
                    Guid.CreateVersion7(),
                    [
                        new JourneyStopDto(1, Guid.CreateVersion7(), true, null, DepartAt, Guid.CreateVersion7()),
                        new JourneyStopDto(2, Guid.CreateVersion7(), true, DepartAt.AddMinutes(10), DepartAt.AddMinutes(11), null),
                    ]),
                journeyId);

            await database.SaveChangesAsync();
        }

        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (await CountLiveStatesAsync(journeyId) == 0 && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        Assert.Equal(1, await CountLiveStatesAsync(journeyId));
    }

    private Task<HttpResponseMessage> PostFactAsync(
        Guid journeyId,
        int stopSequence,
        DateTimeOffset departedAt,
        Guid staffId,
        string? mode)
    {
        var route = $"{TestEndpoints.JourneyFactRoute}"
            + $"?journeyId={journeyId}"
            + $"&stopSequence={stopSequence}"
            + $"&departedAt={Uri.EscapeDataString(departedAt.ToString("O"))}"
            + $"&staffId={staffId}"
            + (mode is null ? string.Empty : $"&mode={mode}");

        return _client.PostAsync(route, content: null);
    }

    private async Task<int> CountLiveStatesAsync(Guid journeyId)
    {
        var count = Assert.Single(await _postgres.QueryAsync(
            $"SELECT count(*) FROM operations.journey_live_states WHERE journey_id = '{journeyId}'",
            reader => reader.GetInt64(0)));

        return (int)count;
    }

    private async Task<int> CountJourneyEventsAsync(Guid journeyId)
    {
        var count = Assert.Single(await _postgres.QueryAsync(
            $"SELECT count(*) FROM operations.journey_events WHERE journey_id = '{journeyId}'",
            reader => reader.GetInt64(0)));

        return (int)count;
    }

    private async Task<int> CountOutboxMessagesAsync(Guid journeyId)
    {
        var count = Assert.Single(await _postgres.QueryAsync(
            $"SELECT count(*) FROM operations.outbox_messages WHERE ordering_key = '{journeyId}'",
            reader => reader.GetInt64(0)));

        return (int)count;
    }

    private async Task<long> CountAuditEntriesAsync(Guid staffId)
    {
        return Assert.Single(await _postgres.QueryAsync(
            $"SELECT count(*) FROM audit.entries WHERE actor_staff_id = '{staffId}'",
            reader => reader.GetInt64(0)));
    }

    private async Task<bool> AuditEntryExistsAsync(Guid entryId)
    {
        return Assert.Single(await _postgres.QueryAsync(
            $"SELECT count(*) FROM audit.entries WHERE id = '{entryId}'",
            reader => reader.GetInt64(0))) == 1;
    }

    private async Task<int> LastSequenceNumberAsync(Guid journeyId)
    {
        var sequenceNumber = Assert.Single(await _postgres.QueryAsync(
            $"SELECT last_sequence_number FROM operations.journey_live_states WHERE journey_id = '{journeyId}'",
            reader => reader.GetInt64(0)));

        return (int)sequenceNumber;
    }

    private async Task<DateTimeOffset?> LastDepartedAtAsync(Guid journeyId, int stopSequence)
    {
        return Assert.Single(await _postgres.QueryAsync(
            $"""
            SELECT actual_departure
            FROM operations.journey_stop_live_states
            WHERE journey_id = '{journeyId}' AND sequence = {stopSequence}
            """,
            reader => reader.IsDBNull(0) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(0)));
    }

    private async Task<LiveStateRow> LiveStateAsync(Guid journeyId)
    {
        return Assert.Single(await _postgres.QueryAsync(
            $"""
            SELECT lifecycle_status, last_sequence_number, last_updated_at
            FROM operations.journey_live_states
            WHERE journey_id = '{journeyId}'
            """,
            reader => new LiveStateRow(
                reader.GetString(0),
                (int)reader.GetInt64(1),
                reader.GetFieldValue<DateTimeOffset>(2))));
    }

    /// <summary>
    /// Removes the rows this test created. The audit record is append-only, so its trigger is
    /// disabled for the delete and enabled again, as the neighbouring audit tests do.
    /// </summary>
    private async Task CleanUpAsync(Guid journeyId, params Guid[] staffIds)
    {
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

        var actors = string.Join(", ", staffIds.Select(staffId => $"'{staffId}'"));

        await _postgres.ExecuteAsync("ALTER TABLE audit.entries DISABLE TRIGGER entries_append_only");

        try
        {
            await _postgres.ExecuteAsync($"DELETE FROM audit.entries WHERE actor_staff_id IN ({actors})");
        }
        finally
        {
            await _postgres.ExecuteAsync("ALTER TABLE audit.entries ENABLE TRIGGER entries_append_only");
        }
    }

    private sealed record LiveStateRow(string Status, int LastSequenceNumber, DateTimeOffset LastUpdatedAt);
}
