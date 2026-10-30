using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using SmartRail.Host.Http;
using SmartRail.Host.IntegrationTests.Infrastructure;
using SmartRail.SharedKernel.Application.Abstractions;

namespace SmartRail.Host.IntegrationTests.Audit;

/// <summary>
/// The Auditor's chain integrity check (audit design D6, AUD-05) and the record's promise that it
/// holds no passenger data (AUD-07, M-03). An untouched record verifies with its count, a row edited
/// directly in the database — with the append-only trigger disabled for the edit and enabled again —
/// is caught at exactly its position both when its content and when its link was altered, an empty
/// record is intact with zero entries, and only the Auditor may ask.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AuditIntegrityTests : IDisposable
{
    /// <summary>The seeded auditor account (seed/identity/staff.json).</summary>
    private const string AuditorSubject = "auditor.wasantha";

    /// <summary>The seeded controller account (seed/identity/staff.json).</summary>
    private const string ControllerSubject = "controller.mahesh";

    /// <summary>The seeded guard account (seed/identity/staff.json).</summary>
    private const string GuardSubject = "guard.ishara";

    /// <summary>A JWT always starts with this, so no entry may contain it.</summary>
    private const string TokenPrefix = "eyJ";

    /// <summary>A provider key is <c>srk_</c>, eight hex digits, an underscore and thirty-two more.</summary>
    private const string ApiKeyPattern = "srk_[0-9a-f]{8}_[0-9a-f]{32}";

    /// <summary>Four dotted decimal octets.</summary>
    private const string IpAddressV4Pattern = @"\b(?:\d{1,3}\.){3}\d{1,3}\b";

    /// <summary>Three or more colon-separated hexadecimal groups, which no timestamp has.</summary>
    private const string IpAddressV6Pattern = @"(?:[0-9a-f]{0,4}:){3,}[0-9a-f]{0,4}";

    private const string IntegrityRoute = $"{ApiRoutes.BasePath}/audit/integrity";

    /// <summary>How long the denial's entry may take to appear, well past the poll and retry delays.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private readonly PostgresFixture _postgres;
    private readonly SmartRailApiFactory _factory;
    private readonly HttpClient _client;

    public AuditIntegrityTests(PostgresFixture postgres)
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
    public async Task An_untouched_record_is_intact_with_the_entry_count()
    {
        await StartAsync();
        await ResetAsync();

        try
        {
            await RecordAsync();
            await RecordAsync();
            await RecordAsync();

            using var response = await GetIntegrityAsync(AuditorToken());
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using var document = await ReadJsonAsync(response);
            Assert.Equal("intact", document.RootElement.GetProperty("status").GetString());
            Assert.Equal(3L, document.RootElement.GetProperty("entriesChecked").GetInt64());
            Assert.False(
                document.RootElement.TryGetProperty("firstFailingPosition", out _),
                "An intact record should not name a failing position.");
        }
        finally
        {
            await ResetAsync();
        }
    }

    [Fact]
    public async Task An_empty_record_is_intact_with_zero_entries()
    {
        await StartAsync();
        await ResetAsync();

        try
        {
            using var response = await GetIntegrityAsync(AuditorToken());
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using var document = await ReadJsonAsync(response);
            Assert.Equal("intact", document.RootElement.GetProperty("status").GetString());
            Assert.Equal(0L, document.RootElement.GetProperty("entriesChecked").GetInt64());
            Assert.False(document.RootElement.TryGetProperty("firstFailingPosition", out _));
        }
        finally
        {
            await ResetAsync();
        }
    }

    [Fact]
    public async Task An_edited_after_value_is_broken_at_that_entries_position()
    {
        await StartAsync();
        await ResetAsync();

        try
        {
            await RecordAsync();
            var edited = await RecordAsync();
            await RecordAsync();

            var position = await PositionOfAsync(edited);
            Assert.Equal(2L, position);

            await EditAfterAsync(edited, """{"status":"cancelled"}""");

            using var response = await GetIntegrityAsync(AuditorToken());
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using var document = await ReadJsonAsync(response);
            Assert.Equal("broken", document.RootElement.GetProperty("status").GetString());
            Assert.Equal(position, document.RootElement.GetProperty("firstFailingPosition").GetInt64());
            Assert.Equal(position, document.RootElement.GetProperty("entriesChecked").GetInt64());
        }
        finally
        {
            await ResetAsync();
        }
    }

    [Fact]
    public async Task An_edited_previous_hash_is_broken_at_that_entries_position()
    {
        await StartAsync();
        await ResetAsync();

        try
        {
            await RecordAsync();
            var edited = await RecordAsync();
            await RecordAsync();

            var position = await PositionOfAsync(edited);
            Assert.Equal(2L, position);

            await EditAsync(
                $"UPDATE audit.entries SET previous_hash = '{new string('a', 64)}' WHERE id = '{edited}'");

            using var response = await GetIntegrityAsync(AuditorToken());
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using var document = await ReadJsonAsync(response);
            Assert.Equal("broken", document.RootElement.GetProperty("status").GetString());
            Assert.Equal(position, document.RootElement.GetProperty("firstFailingPosition").GetInt64());
            Assert.Equal(position, document.RootElement.GetProperty("entriesChecked").GetInt64());
        }
        finally
        {
            await ResetAsync();
        }
    }

    [Fact]
    public async Task A_controller_or_guard_token_is_forbidden()
    {
        await StartAsync();
        await ResetAsync();

        try
        {
            foreach (var token in new[] { ControllerToken(), GuardToken() })
            {
                using var response = await GetIntegrityAsync(token);
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            }
        }
        finally
        {
            await ResetAsync();
        }
    }

    [Fact]
    public async Task A_request_with_no_token_is_unauthorized()
    {
        await StartAsync();
        await ResetAsync();

        try
        {
            using var response = await GetIntegrityAsync();
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }
        finally
        {
            await ResetAsync();
        }
    }

    /// <summary>
    /// The spec's automated check for AUD-07 and M-03: every entry the suite can produce — a staff
    /// change recorded through the recorder and a denial recorded by the consumer — holds no token,
    /// no provider API key and no IP address in any of its text columns.
    /// </summary>
    [Fact]
    public async Task No_entry_holds_a_token_an_api_key_or_an_address()
    {
        await StartAsync();
        await ResetAsync();
        Guid? eventId = null;

        try
        {
            await RecordAsync();
            await RecordAsync();

            eventId = await DenyAsync();
            await WaitForDenialEntryAsync(eventId.Value);

            var rows = await _postgres.QueryAsync(
                """
                SELECT chain_position, actor_role, action, target_type, target_id,
                       before::text, after::text, previous_hash, entry_hash
                FROM audit.entries
                ORDER BY chain_position
                """,
                reader => string.Join(
                    " | ",
                    Enumerable.Range(0, reader.FieldCount)
                        .Select(index => reader.IsDBNull(index) ? string.Empty : reader.GetValue(index).ToString())));

            // Two recorded changes and the denial, so the check is not vacuous.
            Assert.Equal(3, rows.Count);

            foreach (var row in rows)
            {
                Assert.DoesNotContain(TokenPrefix, row, StringComparison.Ordinal);
                Assert.DoesNotMatch(ApiKeyPattern, row);
                Assert.DoesNotMatch(IpAddressV4Pattern, row);
                Assert.DoesNotMatch(IpAddressV6Pattern, row);
            }
        }
        finally
        {
            await CleanUpDenialAsync(eventId);
            await ResetAsync();
        }
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private static string AuditorToken() => TestTokens.Create(AuditorSubject, Roles.Auditor);

    private static string ControllerToken() => TestTokens.Create(ControllerSubject, Roles.NetworkController);

    private static string GuardToken() => TestTokens.Create(GuardSubject, Roles.Guard);

    /// <summary>Starts the Host once so its migrations, including the trigger, are applied.</summary>
    private async Task StartAsync()
    {
        using var response = await _client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<HttpResponseMessage> GetIntegrityAsync(string? token = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, IntegrityRoute);

        if (token is not null)
        {
            request.WithStaffToken(token);
        }

        return await _client.SendAsync(request);
    }

    /// <summary>
    /// Records one entry through the test-only recorder route, which is how the production recorder
    /// is exercised while no state-changing endpoint exists yet, and returns the entry's ID.
    /// </summary>
    private async Task<Guid> RecordAsync()
    {
        using var response = await _client.PostAsync(
            $"{TestEndpoints.AuditRecordRoute}?staffId={Guid.CreateVersion7()}&action=publish_delay",
            content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<AuditRecordResponse>();
        Assert.NotNull(body);

        return body.EntryId;
    }

    /// <summary>Refuses a Guard's write on a controller-only endpoint and returns the event's ID.</summary>
    private async Task<Guid> DenyAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TestEndpoints.ControllerWriteRoute)
            .WithStaffToken(GuardToken());

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

    private async Task<long> PositionOfAsync(Guid entryId) => Assert.Single(await _postgres.QueryAsync(
        $"SELECT chain_position FROM audit.entries WHERE id = '{entryId}'",
        reader => reader.GetInt64(0)));

    /// <summary>
    /// Edits one entry's after-value the way a direct SQL edit does. The table is append-only, so the
    /// trigger is disabled for the edit and enabled again straight after (design D4).
    /// </summary>
    private Task EditAfterAsync(Guid entryId, string after) =>
        EditAsync($"UPDATE audit.entries SET after = '{after}'::jsonb WHERE id = '{entryId}'");

    private async Task EditAsync(string sql)
    {
        await _postgres.ExecuteAsync("ALTER TABLE audit.entries DISABLE TRIGGER entries_append_only");

        try
        {
            await _postgres.ExecuteAsync(sql);
        }
        finally
        {
            await _postgres.ExecuteAsync("ALTER TABLE audit.entries ENABLE TRIGGER entries_append_only");
        }
    }

    /// <summary>Waits until the denial's consumer has written its entry, which happens shortly after.</summary>
    private async Task WaitForDenialEntryAsync(Guid eventId)
    {
        var deadline = DateTimeOffset.UtcNow.Add(Timeout);

        while (true)
        {
            var count = Assert.Single(await _postgres.QueryAsync(
                $"SELECT count(*) FROM audit.entries WHERE source_event_id = '{eventId}'",
                reader => reader.GetInt64(0)));

            if (count > 0)
            {
                return;
            }

            Assert.True(
                DateTimeOffset.UtcNow < deadline,
                $"The denial's audit entry did not appear within {Timeout}.");

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
    }

    /// <summary>Removes the denial's outbox rows, so the shared database is left as it was found.</summary>
    private async Task CleanUpDenialAsync(Guid? eventId)
    {
        if (eventId is not { } id)
        {
            return;
        }

        await _postgres.ExecuteAsync(
            $"DELETE FROM identity.outbox_deliveries WHERE message_id = '{id}'");
        await _postgres.ExecuteAsync($"DELETE FROM identity.outbox_messages WHERE id = '{id}'");
    }

    /// <summary>
    /// Empties the shared record so the chain starts at position 1. The table is append-only, so the
    /// trigger is disabled for the delete and enabled again before the test writes; the other test
    /// classes share this database, which is why the rows are removed again in each <c>finally</c>.
    /// </summary>
    private async Task ResetAsync()
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
