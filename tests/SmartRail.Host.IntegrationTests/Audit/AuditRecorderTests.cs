using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SmartRail.Audit.Contracts;
using SmartRail.Audit.Domain.Entries;
using SmartRail.Audit.Infrastructure.Persistence;
using SmartRail.Host.IntegrationTests.Infrastructure;

namespace SmartRail.Host.IntegrationTests.Audit;

/// <summary>
/// The recorder and the advisory-locked append (audit design D1, D2, D3): entries are chained by a
/// gap-free position, concurrent recordings queue on the chain's lock, an entry commits with the
/// caller's change or rolls back with it, and a recording outside a request transaction is refused.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AuditRecorderTests : IDisposable
{
    private readonly PostgresFixture _postgres;
    private readonly SmartRailApiFactory _factory;
    private readonly HttpClient _client;

    public AuditRecorderTests(PostgresFixture postgres)
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
    public async Task Two_recorded_entries_get_consecutive_positions_and_link_by_hash()
    {
        await StartAsync();
        await ResetAsync();

        try
        {
            var firstId = await RecordAsync();
            var secondId = await RecordAsync();

            var entries = await LoadAsync();
            var first = Assert.Single(entries, entry => entry.Id == firstId);
            var second = Assert.Single(entries, entry => entry.Id == secondId);

            Assert.Equal(1, first.ChainPosition);
            Assert.Equal(2, second.ChainPosition);
            Assert.Equal(AuditEntryHash.GenesisPreviousHash, first.PreviousHash);
            Assert.Equal(first.EntryHash, second.PreviousHash);
            Assert.Equal(first.EntryHash, first.RecomputeHash());
            Assert.Equal(second.EntryHash, second.RecomputeHash());
        }
        finally
        {
            await ResetAsync();
        }
    }

    [Fact]
    public async Task Ten_concurrent_recordings_share_a_gap_free_chain()
    {
        await StartAsync();
        await ResetAsync();

        try
        {
            var responses = await Task.WhenAll(Enumerable.Range(0, 10)
                .Select(_ => PostRecordAsync()));

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

            var entries = await LoadAsync();

            Assert.Equal(
                Enumerable.Range(1, 10).Select(position => (long)position),
                entries.Select(entry => entry.ChainPosition));

            var previousHash = AuditEntryHash.GenesisPreviousHash;

            foreach (var entry in entries)
            {
                Assert.Equal(previousHash, entry.PreviousHash);
                Assert.Equal(entry.EntryHash, entry.RecomputeHash());
                previousHash = entry.EntryHash;
            }
        }
        finally
        {
            await ResetAsync();
        }
    }

    [Theory]
    [InlineData("throw")]
    [InlineData("500")]
    public async Task A_failed_request_leaves_no_entry_and_rolls_back_the_caller_change(string mode)
    {
        await StartAsync();
        await ResetAsync();
        var lineId = Guid.CreateVersion7();

        try
        {
            using var response = await _client.PostAsync(
                $"{TestEndpoints.AuditRecordRoute}?staffId={Guid.CreateVersion7()}&lineId={lineId}&mode={mode}",
                content: null);

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Empty(await LoadAsync());
            Assert.False(await LineExistsAsync(lineId), "The caller's network write should have rolled back.");
        }
        finally
        {
            await ResetAsync();
            await _postgres.ExecuteAsync($"DELETE FROM network.lines WHERE id = '{lineId}'");
        }
    }

    [Fact]
    public async Task Recording_outside_a_request_transaction_throws()
    {
        await StartAsync();

        using var scope = _factory.Services.CreateScope();
        var recorder = scope.ServiceProvider.GetRequiredService<IAuditRecorder>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => recorder.RecordAsync(
            new AuditRecordRequest(
                Guid.CreateVersion7(),
                "network_controller",
                "publish_delay",
                "journey",
                Guid.CreateVersion7().ToString(),
                null,
                """{"status":"delayed"}""",
                null)));

        Assert.Contains("state-changing request", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Starts the Host once so its migrations, including the trigger, are applied.</summary>
    private async Task StartAsync()
    {
        using var response = await _client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private Task<HttpResponseMessage> PostRecordAsync() =>
        _client.PostAsync(
            $"{TestEndpoints.AuditRecordRoute}?staffId={Guid.CreateVersion7()}&action=publish_delay",
            content: null);

    private async Task<Guid> RecordAsync()
    {
        using var response = await PostRecordAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<AuditRecordResponse>();
        Assert.NotNull(body);

        return body.EntryId;
    }

    private async Task<IReadOnlyList<AuditEntry>> LoadAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        return await database.Entries
            .AsNoTracking()
            .OrderBy(entry => entry.ChainPosition)
            .ToListAsync();
    }

    private async Task<bool> LineExistsAsync(Guid id) =>
        Assert.Single(await _postgres.QueryAsync(
            $"SELECT count(*) FROM network.lines WHERE id = '{id}'",
            reader => reader.GetInt64(0))) == 1;

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
