using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartRail.Audit.Domain.Entries;
using SmartRail.Audit.Infrastructure.Persistence;
using SmartRail.Host.Http;
using SmartRail.Host.IntegrationTests.Infrastructure;
using SmartRail.SharedKernel.Application.Abstractions;

namespace SmartRail.Host.IntegrationTests.Audit;

/// <summary>
/// The Auditor's read-only endpoints (audit design D6, AUD-01 to AUD-03, AUD-06): the list is newest
/// first and filtered by actor, time range and outcome, paging stops at 100 entries, a bad filter is a
/// 400, the detail returns the before and after values and the entry a correction points at, and only
/// the Auditor may read any of it through a GET.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AuditEndpointsTests : IDisposable
{
    /// <summary>The seeded auditor account (seed/identity/staff.json).</summary>
    private const string AuditorSubject = "auditor.wasantha";

    /// <summary>The seeded controller account (seed/identity/staff.json).</summary>
    private const string ControllerSubject = "controller.mahesh";

    /// <summary>The seeded guard account (seed/identity/staff.json).</summary>
    private const string GuardSubject = "guard.ishara";

    private const string EntriesRoute = $"{ApiRoutes.BasePath}/audit/entries";

    /// <summary>A fixed start time, so a range filter never depends on when the test runs.</summary>
    private static readonly DateTimeOffset BaseTime = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);

    private readonly PostgresFixture _postgres;
    private readonly SmartRailApiFactory _factory;
    private readonly HttpClient _client;

    public AuditEndpointsTests(PostgresFixture postgres)
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
    public async Task The_list_is_ordered_newest_first_by_chain_position()
    {
        await StartAsync();
        await ResetAsync();

        try
        {
            var actor = Guid.CreateVersion7();
            var oldest = await InsertEntryAsync(BaseTime, actor, "publish_delay");
            var middle = await InsertEntryAsync(BaseTime.AddHours(1), actor, "publish_cancellation");
            var newest = await InsertEntryAsync(BaseTime.AddHours(2), actor, "publish_platform_change");

            using var response = await GetAsync(EntriesRoute, AuditorToken());
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using var document = await ReadJsonAsync(response);
            var items = Items(document);

            Assert.Equal(new[] { newest, middle, oldest }, items.Select(item => item.GetProperty("id").GetGuid()).ToArray());
            Assert.Equal(new[] { 3L, 2L, 1L }, items.Select(item => item.GetProperty("chainPosition").GetInt64()).ToArray());
            Assert.Equal(3, document.RootElement.GetProperty("totalCount").GetInt32());
            Assert.EndsWith("Z", items[0].GetProperty("occurredAt").GetString()!, StringComparison.Ordinal);
        }
        finally
        {
            await ResetAsync();
        }
    }

    [Fact]
    public async Task The_list_filters_by_actor()
    {
        await StartAsync();
        await ResetAsync();

        try
        {
            var guard = Guid.CreateVersion7();
            var otherGuard = Guid.CreateVersion7();

            await InsertEntryAsync(BaseTime, guard, "departure_logged");
            await InsertEntryAsync(BaseTime.AddHours(1), otherGuard, "departure_logged");
            await InsertEntryAsync(BaseTime.AddHours(2), guard, "departure_logged");

            using var response = await GetAsync($"{EntriesRoute}?actorId={guard}", AuditorToken());
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using var document = await ReadJsonAsync(response);
            var items = Items(document);

            Assert.Equal(2, items.Count);
            Assert.Equal(2, document.RootElement.GetProperty("totalCount").GetInt32());
            Assert.All(items, item => Assert.Equal(guard, item.GetProperty("actorStaffId").GetGuid()));
        }
        finally
        {
            await ResetAsync();
        }
    }

    [Fact]
    public async Task The_list_filters_by_time_range_inclusively()
    {
        await StartAsync();
        await ResetAsync();

        try
        {
            var actor = Guid.CreateVersion7();

            await InsertEntryAsync(BaseTime, actor, "publish_delay");
            var inRange = await InsertEntryAsync(BaseTime.AddHours(1), actor, "publish_delay");
            await InsertEntryAsync(BaseTime.AddHours(2), actor, "publish_delay");

            using var window = await GetAsync(
                $"{EntriesRoute}?from={Timestamp(BaseTime.AddMinutes(30))}&to={Timestamp(BaseTime.AddMinutes(90))}",
                AuditorToken());
            Assert.Equal(HttpStatusCode.OK, window.StatusCode);

            using var windowDocument = await ReadJsonAsync(window);
            Assert.Equal(
                new[] { inRange },
                Items(windowDocument).Select(item => item.GetProperty("id").GetGuid()).ToArray());

            // The bounds are inclusive, so a range that is exactly the entry's own instant matches it.
            using var exact = await GetAsync(
                $"{EntriesRoute}?from={Timestamp(BaseTime.AddHours(1))}&to={Timestamp(BaseTime.AddHours(1))}",
                AuditorToken());
            Assert.Equal(HttpStatusCode.OK, exact.StatusCode);

            using var exactDocument = await ReadJsonAsync(exact);
            Assert.Equal(
                new[] { inRange },
                Items(exactDocument).Select(item => item.GetProperty("id").GetGuid()).ToArray());
        }
        finally
        {
            await ResetAsync();
        }
    }

    [Fact]
    public async Task The_list_filters_by_outcome()
    {
        await StartAsync();
        await ResetAsync();

        try
        {
            var actor = Guid.CreateVersion7();
            var succeeded = await InsertEntryAsync(BaseTime, actor, "publish_delay");
            var denied = await InsertEntryAsync(
                BaseTime.AddHours(1),
                actor,
                "publish_delay",
                AuditOutcome.Denied,
                Roles.Guard,
                targetType: null);

            using var deniedResponse = await GetAsync($"{EntriesRoute}?outcome=denied", AuditorToken());
            Assert.Equal(HttpStatusCode.OK, deniedResponse.StatusCode);

            using var deniedDocument = await ReadJsonAsync(deniedResponse);
            var deniedItems = Items(deniedDocument);
            Assert.Equal(
                new[] { denied },
                deniedItems.Select(item => item.GetProperty("id").GetGuid()).ToArray());
            Assert.Equal("denied", deniedItems[0].GetProperty("outcome").GetString());

            using var succeededResponse = await GetAsync($"{EntriesRoute}?outcome=succeeded", AuditorToken());
            Assert.Equal(HttpStatusCode.OK, succeededResponse.StatusCode);

            using var succeededDocument = await ReadJsonAsync(succeededResponse);
            Assert.Equal(
                new[] { succeeded },
                Items(succeededDocument).Select(item => item.GetProperty("id").GetGuid()).ToArray());
        }
        finally
        {
            await ResetAsync();
        }
    }

    [Fact]
    public async Task The_list_pages_and_reports_the_totals()
    {
        await StartAsync();
        await ResetAsync();

        try
        {
            var actor = Guid.CreateVersion7();
            var oldest = await InsertEntryAsync(BaseTime, actor, "publish_delay");
            var middle = await InsertEntryAsync(BaseTime.AddHours(1), actor, "publish_delay");
            var newest = await InsertEntryAsync(BaseTime.AddHours(2), actor, "publish_delay");

            using var firstPage = await GetAsync($"{EntriesRoute}?page=1&pageSize=2", AuditorToken());
            Assert.Equal(HttpStatusCode.OK, firstPage.StatusCode);

            using var firstDocument = await ReadJsonAsync(firstPage);
            Assert.Equal(
                new[] { newest, middle },
                Items(firstDocument).Select(item => item.GetProperty("id").GetGuid()).ToArray());
            Assert.Equal(1, firstDocument.RootElement.GetProperty("page").GetInt32());
            Assert.Equal(2, firstDocument.RootElement.GetProperty("pageSize").GetInt32());
            Assert.Equal(3, firstDocument.RootElement.GetProperty("totalCount").GetInt32());
            Assert.Equal(2, firstDocument.RootElement.GetProperty("totalPages").GetInt32());
            Assert.True(firstDocument.RootElement.GetProperty("hasNextPage").GetBoolean());
            Assert.False(firstDocument.RootElement.GetProperty("hasPreviousPage").GetBoolean());

            using var secondPage = await GetAsync($"{EntriesRoute}?page=2&pageSize=2", AuditorToken());
            Assert.Equal(HttpStatusCode.OK, secondPage.StatusCode);

            using var secondDocument = await ReadJsonAsync(secondPage);
            Assert.Equal(
                new[] { oldest },
                Items(secondDocument).Select(item => item.GetProperty("id").GetGuid()).ToArray());
            Assert.False(secondDocument.RootElement.GetProperty("hasNextPage").GetBoolean());
            Assert.True(secondDocument.RootElement.GetProperty("hasPreviousPage").GetBoolean());
        }
        finally
        {
            await ResetAsync();
        }
    }

    [Fact]
    public async Task A_page_size_of_100_is_accepted_and_101_is_refused()
    {
        await StartAsync();
        await ResetAsync();

        try
        {
            await InsertEntryAsync(BaseTime, Guid.CreateVersion7(), "publish_delay");

            using var largest = await GetAsync($"{EntriesRoute}?pageSize=100", AuditorToken());
            Assert.Equal(HttpStatusCode.OK, largest.StatusCode);

            using var largestDocument = await ReadJsonAsync(largest);
            Assert.Equal(100, largestDocument.RootElement.GetProperty("pageSize").GetInt32());

            using var tooLarge = await GetAsync($"{EntriesRoute}?pageSize=101", AuditorToken());
            Assert.Equal(HttpStatusCode.BadRequest, tooLarge.StatusCode);
            Assert.Equal("application/problem+json", tooLarge.Content.Headers.ContentType?.MediaType);

            using var problem = await ReadJsonAsync(tooLarge);
            Assert.True(
                problem.RootElement.GetProperty("errors").TryGetProperty("pageSize", out var messages),
                "The refusal should name the pageSize field.");
            Assert.NotEmpty(messages.EnumerateArray());
        }
        finally
        {
            await ResetAsync();
        }
    }

    [Fact]
    public async Task A_range_whose_start_is_after_its_end_is_refused()
    {
        await StartAsync();
        await ResetAsync();

        try
        {
            using var response = await GetAsync(
                $"{EntriesRoute}?from={Timestamp(BaseTime.AddHours(2))}&to={Timestamp(BaseTime.AddHours(1))}",
                AuditorToken());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

            using var problem = await ReadJsonAsync(response);
            Assert.True(
                problem.RootElement.GetProperty("errors").TryGetProperty("from", out _),
                "The refusal should name the from field.");
        }
        finally
        {
            await ResetAsync();
        }
    }

    [Fact]
    public async Task An_unknown_outcome_is_refused()
    {
        await StartAsync();
        await ResetAsync();

        try
        {
            using var response = await GetAsync($"{EntriesRoute}?outcome=cancelled", AuditorToken());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

            using var problem = await ReadJsonAsync(response);
            Assert.True(
                problem.RootElement.GetProperty("errors").TryGetProperty("outcome", out _),
                "The refusal should name the outcome field.");
        }
        finally
        {
            await ResetAsync();
        }
    }

    [Fact]
    public async Task The_detail_returns_the_before_and_after_values_and_the_related_entry()
    {
        await StartAsync();
        await ResetAsync();

        try
        {
            var actor = Guid.CreateVersion7();

            var original = await InsertEntryAsync(
                BaseTime,
                actor,
                "publish_delay",
                before: """{"status":"scheduled"}""",
                after: """{"status":"delayed","minutes":5}""");

            var correction = await InsertEntryAsync(
                BaseTime.AddMinutes(2),
                actor,
                "correct_delay",
                before: """{"status":"delayed","minutes":5}""",
                after: """{"status":"scheduled"}""",
                relatedEntryId: original);

            using var response = await GetAsync($"{EntriesRoute}/{correction}", AuditorToken());
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using var document = await ReadJsonAsync(response);
            var entry = document.RootElement;

            Assert.Equal(correction, entry.GetProperty("id").GetGuid());
            Assert.Equal(original, entry.GetProperty("relatedEntryId").GetGuid());
            Assert.Equal("correct_delay", entry.GetProperty("action").GetString());
            Assert.Equal("succeeded", entry.GetProperty("outcome").GetString());

            // The values are JSON objects, not strings of JSON.
            var before = entry.GetProperty("before");
            var after = entry.GetProperty("after");
            Assert.Equal(JsonValueKind.Object, before.ValueKind);
            Assert.Equal(JsonValueKind.Object, after.ValueKind);
            Assert.Equal("delayed", before.GetProperty("status").GetString());
            Assert.Equal(5, before.GetProperty("minutes").GetInt32());
            Assert.Equal("scheduled", after.GetProperty("status").GetString());

            // The original it corrects is unchanged.
            using var originalResponse = await GetAsync($"{EntriesRoute}/{original}", AuditorToken());
            Assert.Equal(HttpStatusCode.OK, originalResponse.StatusCode);

            using var originalDocument = await ReadJsonAsync(originalResponse);
            Assert.Equal(
                "delayed",
                originalDocument.RootElement.GetProperty("after").GetProperty("status").GetString());
            Assert.Equal(5, originalDocument.RootElement.GetProperty("after").GetProperty("minutes").GetInt32());
        }
        finally
        {
            await ResetAsync();
        }
    }

    [Fact]
    public async Task An_unknown_entry_is_not_found()
    {
        await StartAsync();
        await ResetAsync();

        try
        {
            using var response = await GetAsync($"{EntriesRoute}/{Guid.CreateVersion7()}", AuditorToken());

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

            using var problem = await ReadJsonAsync(response);
            Assert.Equal(404, problem.RootElement.GetProperty("status").GetInt32());
        }
        finally
        {
            await ResetAsync();
        }
    }

    [Fact]
    public async Task A_controller_or_guard_token_is_forbidden_on_both_endpoints()
    {
        await StartAsync();
        await ResetAsync();

        try
        {
            var id = await InsertEntryAsync(BaseTime, Guid.CreateVersion7(), "publish_delay");

            foreach (var token in new[] { ControllerToken(), GuardToken() })
            {
                using var list = await GetAsync(EntriesRoute, token);
                Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
                Assert.Equal("application/problem+json", list.Content.Headers.ContentType?.MediaType);

                using var detail = await GetAsync($"{EntriesRoute}/{id}", token);
                Assert.Equal(HttpStatusCode.Forbidden, detail.StatusCode);
                Assert.Equal("application/problem+json", detail.Content.Headers.ContentType?.MediaType);
            }
        }
        finally
        {
            await ResetAsync();
        }
    }

    [Fact]
    public async Task A_request_with_no_token_is_unauthorized_on_both_endpoints()
    {
        await StartAsync();
        await ResetAsync();

        try
        {
            var id = await InsertEntryAsync(BaseTime, Guid.CreateVersion7(), "publish_delay");

            using var list = await GetAsync(EntriesRoute);
            Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
            Assert.Equal("application/problem+json", list.Content.Headers.ContentType?.MediaType);

            using var detail = await GetAsync($"{EntriesRoute}/{id}");
            Assert.Equal(HttpStatusCode.Unauthorized, detail.StatusCode);
            Assert.Equal("application/problem+json", detail.Content.Headers.ContentType?.MediaType);
        }
        finally
        {
            await ResetAsync();
        }
    }

    [Fact]
    public async Task Deleting_an_entry_is_refused_and_removes_nothing()
    {
        await StartAsync();
        await ResetAsync();

        try
        {
            var id = await InsertEntryAsync(BaseTime, Guid.CreateVersion7(), "publish_delay");

            using var request = new HttpRequestMessage(HttpMethod.Delete, $"{EntriesRoute}/{id}")
                .WithStaffToken(AuditorToken());
            using var response = await _client.SendAsync(request);

            Assert.True(
                response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.MethodNotAllowed,
                $"A delete should be refused with 403 or 405, but was {(int)response.StatusCode}.");

            using var detail = await GetAsync($"{EntriesRoute}/{id}", AuditorToken());
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
            Assert.Equal(1L, await CountEntriesAsync());
        }
        finally
        {
            await ResetAsync();
        }
    }

    [Fact]
    public async Task The_list_never_returns_the_chain_hashes_or_the_changed_values()
    {
        await StartAsync();
        await ResetAsync();

        try
        {
            await InsertEntryAsync(
                BaseTime,
                Guid.CreateVersion7(),
                "publish_delay",
                targetId: Guid.CreateVersion7().ToString(),
                before: """{"status":"scheduled"}""",
                after: """{"status":"delayed"}""");

            using var response = await GetAsync(EntriesRoute, AuditorToken());
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(body);
            var item = Assert.Single(Items(document));

            var names = item.EnumerateObject()
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            var expected = new[]
            {
                "action",
                "actorRole",
                "actorStaffId",
                "chainPosition",
                "id",
                "occurredAt",
                "outcome",
                "targetId",
                "targetType",
            };

            Assert.Equal(expected.OrderBy(name => name, StringComparer.Ordinal).ToArray(), names);
            Assert.False(item.TryGetProperty("before", out _));
            Assert.False(item.TryGetProperty("after", out _));
            Assert.DoesNotContain("hash", body, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await ResetAsync();
        }
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private static string AuditorToken() => TestTokens.Create(AuditorSubject, Roles.Auditor);

    private static string ControllerToken() => TestTokens.Create(ControllerSubject, Roles.NetworkController);

    private static string GuardToken() => TestTokens.Create(GuardSubject, Roles.Guard);

    /// <summary>The wire form of an instant, which the query binder and the API both accept.</summary>
    private static string Timestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static List<JsonElement> Items(JsonDocument document) =>
        document.RootElement.GetProperty("items").EnumerateArray().ToList();

    /// <summary>Starts the Host once so its migrations, including the trigger, are applied.</summary>
    private async Task StartAsync()
    {
        using var response = await _client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<HttpResponseMessage> GetAsync(string route, string? token = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, route);

        if (token is not null)
        {
            request.WithStaffToken(token);
        }

        return await _client.SendAsync(request);
    }

    /// <summary>
    /// Inserts one entry through the real context, linking it to the last one so the chain stays
    /// valid. A raw insert is what lets the list and detail scenarios choose the actor, the outcome
    /// and the time without a state-changing production endpoint.
    /// </summary>
    private async Task<Guid> InsertEntryAsync(
        DateTimeOffset occurredAt,
        Guid actorStaffId,
        string action,
        AuditOutcome outcome = AuditOutcome.Succeeded,
        string? actorRole = Roles.NetworkController,
        string? targetType = "journey",
        string? targetId = null,
        string? before = null,
        string? after = null,
        Guid? relatedEntryId = null)
    {
        using var scope = _factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        var last = await database.Entries
            .AsNoTracking()
            .OrderByDescending(entry => entry.ChainPosition)
            .Select(entry => new { entry.ChainPosition, entry.EntryHash })
            .FirstOrDefaultAsync();

        var result = AuditEntry.Create(
            Guid.CreateVersion7(),
            (last?.ChainPosition ?? 0) + 1,
            occurredAt,
            actorStaffId,
            actorRole,
            action,
            outcome,
            targetType,
            targetId ?? (outcome == AuditOutcome.Succeeded ? Guid.CreateVersion7().ToString() : null),
            before,
            after,
            relatedEntryId,
            sourceEventId: null,
            last?.EntryHash ?? AuditEntryHash.GenesisPreviousHash);

        Assert.True(result.IsSuccess, $"The entry should be valid, but was '{result.Error}'.");

        database.Entries.Add(result.Value);
        await database.SaveChangesAsync();

        return result.Value.Id;
    }

    private async Task<long> CountEntriesAsync() =>
        Assert.Single(await _postgres.QueryAsync(
            "SELECT count(*) FROM audit.entries",
            reader => reader.GetInt64(0)));

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
