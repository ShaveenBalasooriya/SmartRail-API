using System.Net;
using Microsoft.EntityFrameworkCore;
using SmartRail.Audit.Domain.Entries;
using SmartRail.Audit.Infrastructure.Persistence;
using SmartRail.Host.IntegrationTests.Infrastructure;
using SmartRail.SharedKernel.Application.Abstractions;

namespace SmartRail.Host.IntegrationTests.Audit;

/// <summary>
/// The denied-write consumer (audit design D5): a Guard's refused write becomes exactly one
/// <c>denied</c> entry, a redelivered denial adds nothing, a restart adds nothing, and a write that
/// fails once is retried by the bus instead of losing the denial. The entry holds the guard, the
/// role, the attempted action and the time, and nothing about the passenger.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AccessDeniedConsumerTests : IDisposable
{
    /// <summary>The seeded guard account (seed/identity/staff.json).</summary>
    private const string GuardStaffId = "01a15874-e69d-73b2-b24d-d85d22fdead2";

    /// <summary>The action the refused test endpoint reports, from its endpoint name.</summary>
    private const string RefusedAction = "TestControllerOnlyWrite";

    /// <summary>How long a delivery may take before the test fails, well past the poll and retry delays.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private readonly PostgresFixture _postgres;
    private SmartRailApiFactory _factory;
    private HttpClient _client;

    public AccessDeniedConsumerTests(PostgresFixture postgres)
    {
        _postgres = postgres;
        (_factory, _client) = Start();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task A_guard_refusal_is_recorded_once_as_a_denied_entry()
    {
        await StartAsync();
        await ResetEntriesAsync();
        Guid? eventId = null;

        try
        {
            eventId = await DenyAsync();

            var entry = Assert.Single(await WaitForEntriesAsync(eventId.Value));

            Assert.Equal(AuditOutcome.Denied, entry.Outcome);
            Assert.Equal(Guid.Parse(GuardStaffId), entry.ActorStaffId);
            Assert.Equal(Roles.Guard, entry.ActorRole);
            Assert.Equal(RefusedAction, entry.Action);
            Assert.Equal(eventId, entry.SourceEventId);
            Assert.Null(entry.TargetType);
            Assert.Null(entry.TargetId);
            Assert.Null(entry.Before);
            Assert.Null(entry.After);
            Assert.True(
                entry.OccurredAt > DateTimeOffset.UtcNow.AddMinutes(-1),
                $"OccurredAt {entry.OccurredAt} is not the time of the refusal.");

            await WaitForDeliveryAsync(eventId.Value, attempts: 1);

            Assert.True(
                await IsPublishedAsync(eventId.Value),
                "The denial's outbox row should be marked published.");
        }
        finally
        {
            await CleanUpAsync(eventId);
        }
    }

    [Fact]
    public async Task A_redelivered_denial_adds_no_second_entry()
    {
        await StartAsync();
        await ResetEntriesAsync();
        Guid? eventId = null;

        try
        {
            eventId = await DenyAsync();
            await WaitForEntriesAsync(eventId.Value);
            await WaitForDeliveryAsync(eventId.Value, attempts: 1);

            // Redelivering the same outbox message is what a transport retry looks like, and the
            // payload still carries the same event ID.
            await SetDeliveryPendingAsync(eventId.Value);
            await WaitForDeliveryAsync(eventId.Value, attempts: 2);

            Assert.Single(await LoadEntriesAsync(eventId.Value));
        }
        finally
        {
            await CleanUpAsync(eventId);
        }
    }

    [Fact]
    public async Task A_restart_after_a_denial_leaves_one_entry()
    {
        await StartAsync();
        await ResetEntriesAsync();
        Guid? eventId = null;

        try
        {
            eventId = await DenyAsync();
            await WaitForEntriesAsync(eventId.Value);
            await WaitForDeliveryAsync(eventId.Value, attempts: 1);

            // The denial is pending again when the host is stopped, so the restarted dispatcher
            // really does hand the consumer the same event a second time.
            await SetDeliveryPendingAsync(eventId.Value);
            Stop();
            (_factory, _client) = Start();

            await StartAsync();
            await WaitForDeliveryAsync(eventId.Value, attempts: 2);

            Assert.Single(await LoadEntriesAsync(eventId.Value));
        }
        finally
        {
            await CleanUpAsync(eventId);
        }
    }

    [Fact]
    public async Task A_denial_whose_first_audit_write_fails_is_retried_and_recorded_once()
    {
        await StartAsync();
        await ResetEntriesAsync();
        await InstallFirstWriteFailureAsync();
        Guid? eventId = null;

        try
        {
            eventId = await DenyAsync();

            // The first attempt throws, the transport schedules a retry, and the retry writes.
            await WaitForDeliveryAsync(eventId.Value, attempts: 2);

            Assert.Single(await WaitForEntriesAsync(eventId.Value));
        }
        finally
        {
            await RemoveFirstWriteFailureAsync();
            await CleanUpAsync(eventId);
        }
    }

    private (SmartRailApiFactory Factory, HttpClient Client) Start()
    {
        var factory = new SmartRailApiFactory(_postgres.ConnectionString);

        return (factory, factory.CreateApiClient());
    }

    private void Stop()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    /// <summary>Starts the Host once so its migrations, including the trigger, are applied.</summary>
    private async Task StartAsync()
    {
        using var response = await _client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>Refuses a Guard's write on a controller-only endpoint and returns the event's ID.</summary>
    private async Task<Guid> DenyAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TestEndpoints.ControllerWriteRoute)
            .WithStaffToken(TestTokens.Create("guard.ishara", Roles.Guard));

        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var ids = await _postgres.QueryAsync(
            """
            SELECT id
            FROM identity.outbox_messages
            WHERE event_type = 'AccessDenied'
            ORDER BY position DESC
            LIMIT 1
            """,
            reader => reader.GetGuid(0));

        return Assert.Single(ids);
    }

    private async Task<bool> IsPublishedAsync(Guid eventId) =>
        Assert.Single(await _postgres.QueryAsync(
            $"SELECT published_at FROM identity.outbox_messages WHERE id = '{eventId}'",
            reader => !reader.IsDBNull(0)));

    /// <summary>Waits for the delivery to reach <paramref name="attempts"/> attempts and succeed.</summary>
    private async Task WaitForDeliveryAsync(Guid eventId, int attempts)
    {
        var deadline = DateTimeOffset.UtcNow.Add(Timeout);

        while (true)
        {
            var deliveries = await _postgres.QueryAsync(
                "SELECT status, attempt_count FROM identity.outbox_deliveries "
                + $"WHERE message_id = '{eventId}' AND consumer = 'audit.access-denied'",
                reader => (Status: reader.GetString(0), Attempts: reader.GetInt32(1)));

            if (deliveries is [{ Status: "delivered", Attempts: var count }] && count >= attempts)
            {
                return;
            }

            Assert.True(
                DateTimeOffset.UtcNow < deadline,
                $"The audit consumer did not succeed on attempt {attempts} within {Timeout}. "
                + $"Deliveries seen: {string.Join(", ", deliveries)}");

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
    }

    private async Task<IReadOnlyList<AuditEntry>> WaitForEntriesAsync(Guid eventId)
    {
        var deadline = DateTimeOffset.UtcNow.Add(Timeout);

        while (true)
        {
            var entries = await LoadEntriesAsync(eventId);

            if (entries.Count > 0 || DateTimeOffset.UtcNow >= deadline)
            {
                return entries;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
    }

    private async Task<IReadOnlyList<AuditEntry>> LoadEntriesAsync(Guid eventId)
    {
        using var scope = _factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        return await database.Entries
            .AsNoTracking()
            .Where(entry => entry.SourceEventId == eventId)
            .OrderBy(entry => entry.ChainPosition)
            .ToListAsync();
    }

    private Task<int> SetDeliveryPendingAsync(Guid eventId) =>
        _postgres.ExecuteAsync(
            "UPDATE identity.outbox_deliveries "
            + "SET status = 'pending', delivered_at = NULL, next_attempt_at = NULL "
            + $"WHERE message_id = '{eventId}' AND consumer = 'audit.access-denied'");

    /// <summary>
    /// Makes the next insert into <c>audit.entries</c> throw, using a sequence because a rollback
    /// would undo a flag row: the counter must survive the failed transaction to fail only once.
    /// </summary>
    private async Task InstallFirstWriteFailureAsync()
    {
        await _postgres.ExecuteAsync("CREATE SEQUENCE audit.injected_failure_once");
        await _postgres.ExecuteAsync(
            """
            CREATE FUNCTION audit.inject_audit_failure_once()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $$
            BEGIN
                IF nextval('audit.injected_failure_once') = 1 THEN
                    RAISE EXCEPTION 'Injected first audit write failure';
                END IF;

                RETURN NEW;
            END;
            $$;
            """);
        await _postgres.ExecuteAsync(
            "CREATE TRIGGER entries_injected_failure_once "
            + "BEFORE INSERT ON audit.entries "
            + "FOR EACH ROW EXECUTE FUNCTION audit.inject_audit_failure_once()");
    }

    private async Task RemoveFirstWriteFailureAsync()
    {
        await _postgres.ExecuteAsync(
            "DROP TRIGGER IF EXISTS entries_injected_failure_once ON audit.entries");
        await _postgres.ExecuteAsync("DROP FUNCTION IF EXISTS audit.inject_audit_failure_once()");
        await _postgres.ExecuteAsync("DROP SEQUENCE IF EXISTS audit.injected_failure_once");
    }

    /// <summary>
    /// Removes the entry this test caused and the denial's outbox row, so the shared database is
    /// left as the next test class expects to find it. The table is append-only, so the trigger is
    /// disabled for the delete and enabled again straight after.
    /// </summary>
    private async Task CleanUpAsync(Guid? eventId)
    {
        await ResetEntriesAsync();

        if (eventId is not { } id)
        {
            return;
        }

        await _postgres.ExecuteAsync(
            $"DELETE FROM identity.outbox_deliveries WHERE message_id = '{id}'");
        await _postgres.ExecuteAsync($"DELETE FROM identity.outbox_messages WHERE id = '{id}'");
    }

    private async Task ResetEntriesAsync()
    {
        await _postgres.ExecuteAsync("ALTER TABLE audit.entries DISABLE TRIGGER entries_append_only");

        try
        {
            await _postgres.ExecuteAsync("DELETE FROM audit.entries");
        }
        finally
        {
            await _postgres.ExecuteAsync("ALTER TABLE audit.entries ENABLE TRIGGER entries_append_only");
        }
    }
}
