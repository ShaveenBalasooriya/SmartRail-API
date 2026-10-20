using System.Net;
using SmartRail.Host.IntegrationTests.Infrastructure;

namespace SmartRail.Host.IntegrationTests.Persistence;

[Collection(PostgresCollection.Name)]
public sealed class MigrationTests(PostgresFixture postgres) : IDisposable
{
    private readonly SmartRailApiFactory _factory = new(postgres.ConnectionString);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Every_module_schema_exists_after_start_up()
    {
        await StartAsync();

        var schemas = await postgres.QueryAsync(
            "SELECT nspname FROM pg_namespace WHERE nspname IN ('identity','network','operations','catering','audit') ORDER BY 1",
            reader => reader.GetString(0));

        Assert.Equal(["audit", "catering", "identity", "network", "operations"], schemas);
    }

    [Fact]
    public async Task Every_module_has_its_own_migrations_history_table()
    {
        await StartAsync();

        var historyTables = await postgres.QueryAsync(
            """
            SELECT table_schema || '.' || table_name
            FROM information_schema.tables
            WHERE table_name = '__ef_migrations_history'
            ORDER BY 1
            """,
            reader => reader.GetString(0));

        Assert.Equal(
            [
                "audit.__ef_migrations_history",
                "catering.__ef_migrations_history",
                "identity.__ef_migrations_history",
                "network.__ef_migrations_history",
                "operations.__ef_migrations_history",
            ],
            historyTables);
    }

    [Fact]
    public async Task The_initial_migration_of_every_module_is_applied()
    {
        await StartAsync();

        foreach (var schema in new[] { "identity", "network", "operations", "catering", "audit" })
        {
            var applied = await postgres.QueryAsync(
                $"SELECT \"MigrationId\" FROM \"{schema}\".\"__ef_migrations_history\"",
                reader => reader.GetString(0));

            Assert.Contains(applied, migration => migration.EndsWith("_InitialCreate", StringComparison.Ordinal));
        }
    }

    private async Task StartAsync()
    {
        using var client = _factory.CreateApiClient();
        var response = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
