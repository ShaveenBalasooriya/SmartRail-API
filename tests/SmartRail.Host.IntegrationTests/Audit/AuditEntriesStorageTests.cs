using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartRail.Audit.Domain.Entries;
using SmartRail.Audit.Infrastructure.Persistence;
using SmartRail.Host.IntegrationTests.Infrastructure;

namespace SmartRail.Host.IntegrationTests.Audit;

/// <summary>
/// The storage guarantees of the audit record (design D4): <c>audit.entries</c> refuses updates and
/// deletes at the database, a denial's event ID is unique while many successful changes may have
/// none, chain positions are unique, and the jsonb round trip does not change a stored entry's hash.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AuditEntriesStorageTests(PostgresFixture postgres) : IDisposable
{
    private const string BeforeJson = """{ "z": 1, "a": { "y": 2, "b": 3 } }""";

    private const string AfterJson =
        """{ "status": "delayed", "details": { "minutes": 12, "reason": "signal failure" }, "platform": "3" }""";

    private readonly SmartRailApiFactory _factory = new(postgres.ConnectionString);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task A_stored_entry_cannot_be_updated()
    {
        await StartAsync();
        var id = await InsertEntryAsync();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => postgres.ExecuteAsync(
            $"UPDATE audit.entries SET action = 'tampered' WHERE id = '{id}'"));

        Assert.Contains("append-only", exception.Message, StringComparison.Ordinal);
        Assert.Equal("publish_delay", await ActionOfAsync(id));
    }

    [Fact]
    public async Task A_stored_entry_cannot_be_deleted()
    {
        await StartAsync();
        var id = await InsertEntryAsync();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => postgres.ExecuteAsync(
            $"DELETE FROM audit.entries WHERE id = '{id}'"));

        Assert.Contains("append-only", exception.Message, StringComparison.Ordinal);
        Assert.True(await ExistsAsync(id));
    }

    [Fact]
    public async Task A_jsonb_round_trip_still_matches_the_stored_hash()
    {
        await StartAsync();
        var id = await InsertEntryAsync(before: BeforeJson, after: AfterJson);

        AuditEntry stored;
        using (var scope = _factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
            stored = await database.Entries.AsNoTracking().SingleAsync(entry => entry.Id == id);
        }

        // jsonb reorders keys and strips whitespace, so the stored text is not what was hashed.
        Assert.NotEqual(BeforeJson, stored.Before);
        Assert.NotEqual(AfterJson, stored.After);

        // The outcome is stored by name, not as the enum's number (data model: `succeeded`).
        Assert.Equal("succeeded", Assert.Single(await postgres.QueryAsync(
            $"SELECT outcome FROM audit.entries WHERE id = '{id}'",
            reader => reader.GetString(0))));

        // The canonical form sorts keys recursively, so recomputing from the stored text still
        // produces the hash that was written with the entry.
        Assert.Equal(stored.EntryHash, stored.RecomputeHash());
    }

    [Fact]
    public async Task A_source_event_id_can_be_stored_once_but_many_entries_may_have_none()
    {
        await StartAsync();
        var sourceEventId = Guid.CreateVersion7();

        await InsertEntryAsync(sourceEventId: sourceEventId);

        await Assert.ThrowsAsync<DbUpdateException>(() => InsertEntryAsync(sourceEventId: sourceEventId));

        var completedChangeIds = new[] { await InsertEntryAsync(), await InsertEntryAsync() };
        Assert.Equal(2, await CountAsync(
            $"SELECT count(*) FROM audit.entries WHERE id IN ('{completedChangeIds[0]}','{completedChangeIds[1]}')"));
    }

    [Fact]
    public async Task A_chain_position_is_unique()
    {
        await StartAsync();
        var chainPosition = await NextChainPositionAsync();

        await InsertEntryAsync(chainPosition: chainPosition);

        await Assert.ThrowsAsync<DbUpdateException>(() => InsertEntryAsync(chainPosition: chainPosition));
    }

    /// <summary>Starts the Host once so its migrations, including the trigger, are applied.</summary>
    private async Task StartAsync()
    {
        using var client = _factory.CreateApiClient();
        var response = await client.GetAsync("/health/ready");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// Inserts a successful staff change through the real <see cref="AuditDbContext"/>, the same way
    /// the recorder will once it exists. Every value is unique so the shared database is harmless.
    /// </summary>
    private async Task<Guid> InsertEntryAsync(
        Guid? sourceEventId = null,
        long? chainPosition = null,
        string? before = null,
        string? after = null)
    {
        var result = AuditEntry.Create(
            Guid.CreateVersion7(),
            chainPosition ?? await NextChainPositionAsync(),
            DateTimeOffset.UtcNow,
            Guid.CreateVersion7(),
            "network_controller",
            "publish_delay",
            AuditOutcome.Succeeded,
            "journey",
            Guid.CreateVersion7().ToString(),
            before,
            after,
            null,
            sourceEventId,
            AuditEntryHash.GenesisPreviousHash);

        Assert.True(result.IsSuccess, $"The entry should be valid, but was '{result.Error}'.");

        using var scope = _factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        database.Entries.Add(result.Value);
        await database.SaveChangesAsync();

        return result.Value.Id;
    }

    private async Task<long> NextChainPositionAsync()
    {
        var position = Assert.Single(await postgres.QueryAsync(
            "SELECT COALESCE(MAX(chain_position), 0) + 1 FROM audit.entries",
            reader => reader.GetInt64(0)));

        return position;
    }

    private async Task<string> ActionOfAsync(Guid id) => Assert.Single(await postgres.QueryAsync(
        $"SELECT action FROM audit.entries WHERE id = '{id}'",
        reader => reader.GetString(0)));

    private async Task<bool> ExistsAsync(Guid id) => Assert.Single(await postgres.QueryAsync(
        $"SELECT count(*) FROM audit.entries WHERE id = '{id}'",
        reader => reader.GetInt64(0))) == 1;

    private async Task<int> CountAsync(string sql) =>
        (int)Assert.Single(await postgres.QueryAsync(sql, reader => reader.GetInt64(0)));
}
