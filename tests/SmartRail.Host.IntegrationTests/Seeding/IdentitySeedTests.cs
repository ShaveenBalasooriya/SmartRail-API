using System.Net;
using System.Security.Cryptography;
using System.Text;
using SmartRail.Host.Health;
using SmartRail.Host.IntegrationTests.Infrastructure;

namespace SmartRail.Host.IntegrationTests.Seeding;

/// <summary>
/// Seeding is a start-up path against a real database, so it is checked here rather than by a unit
/// test: the second start is what proves idempotency, and only the database can prove the plaintext
/// key was never stored (design D9, seed-data spec).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class IdentitySeedTests(PostgresFixture postgres)
{
    /// <summary>The secret the tests supply through configuration, never through a seed file.</summary>
    private const string PlaintextProviderKey = "srk_ab12cd34_5f2c8d7e9b1a4c6d3e0f7a2b8c9d1e4f";

    /// <summary>The setting the seeder reads the plaintext key from.</summary>
    private const string ProviderKeySetting = "Catering:ProviderApiKey";

    [Fact]
    public async Task Starting_twice_leaves_one_row_per_seeded_account_and_one_provider_key()
    {
        await StartAsync();
        await StartAsync();

        var usernames = await postgres.QueryAsync(
            "SELECT username FROM identity.staff_members ORDER BY username",
            reader => reader.GetString(0));

        Assert.Equal(["auditor.wasantha", "controller.mahesh", "guard.ishara"], usernames);

        var keys = await postgres.QueryAsync(
            "SELECT key_prefix, key_hash FROM identity.provider_api_keys",
            reader => (reader.GetString(0), reader.GetString(1)));

        var (prefix, hash) = Assert.Single(keys);
        Assert.Equal("ab12cd34", prefix);
        Assert.Equal(
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(PlaintextProviderKey))),
            hash);
    }

    [Fact]
    public async Task The_plaintext_provider_key_appears_nowhere_in_the_database()
    {
        await StartAsync();

        var stored = await postgres.QueryAsync(
            "SELECT key_prefix || '|' || key_hash FROM identity.provider_api_keys",
            reader => reader.GetString(0));

        Assert.DoesNotContain(PlaintextProviderKey, string.Join(";", stored), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_identity_migration_creates_the_staff_and_provider_key_tables()
    {
        await StartAsync();

        var tables = await postgres.QueryAsync(
            """
            SELECT table_schema || '.' || table_name
            FROM information_schema.tables
            WHERE table_schema = 'identity'
              AND table_name IN ('staff_members', 'provider_api_keys')
            ORDER BY 1
            """,
            reader => reader.GetString(0));

        Assert.Equal(["identity.provider_api_keys", "identity.staff_members"], tables);
    }

    private async Task StartAsync()
    {
        using var factory = new SmartRailApiFactory(
            postgres.ConnectionString,
            settings: new Dictionary<string, string>
            {
                [ProviderKeySetting] = PlaintextProviderKey,
            });

        using var client = factory.CreateApiClient();
        using var response = await client.GetAsync(HealthEndpoints.ReadyPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
