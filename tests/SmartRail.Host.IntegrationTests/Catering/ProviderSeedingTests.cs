using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SmartRail.Catering.Contracts;
using SmartRail.Host.Health;
using SmartRail.Host.IntegrationTests.Infrastructure;
using SmartRail.SharedKernel.Application.Abstractions;

namespace SmartRail.Host.IntegrationTests.Catering;

/// <summary>
/// The catering provider is reference data: its row is seeded at start-up, its key is Identity's, and
/// Identity asks Catering whether the provider is active (design D1, D9; provider-data-api spec).
/// These checks run against a real database because only it can prove the row exists once, the
/// directory answers from the table, and the (subscription_id, event_id) index refuses a duplicate
/// (CAT-06).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ProviderSeedingTests : IDisposable
{
    /// <summary>The provider named by <c>seed/catering/providers.json</c>.</summary>
    private static readonly Guid SeededProviderId = Guid.Parse("01a15874-e6a0-7546-9097-3dae27a35461");

    /// <summary>A well-formed provider identifier that no seed file names.</summary>
    private static readonly Guid UnknownProviderId = Guid.Parse("01a15874-e6a1-7fff-8000-000000000001");

    private readonly PostgresFixture _postgres;
    private readonly SmartRailApiFactory _factory;

    public ProviderSeedingTests(PostgresFixture postgres)
    {
        _postgres = postgres;
        _factory = new SmartRailApiFactory(postgres.ConnectionString);
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Starting_the_host_seeds_the_provider_exactly_once()
    {
        await StartAsync();

        var providers = await _postgres.QueryAsync(
            "SELECT id, name, is_active FROM catering.providers",
            reader => (reader.GetGuid(0), reader.GetString(1), reader.GetBoolean(2)));

        var (id, name, isActive) = Assert.Single(providers);
        Assert.Equal(SeededProviderId, id);
        Assert.Equal("RailBites", name);
        Assert.True(isActive);
    }

    [Fact]
    public async Task Running_the_seeder_again_leaves_one_provider()
    {
        await StartAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetServices<IModuleSeeder>()
                .Single(candidate => candidate.Schema == "catering");

            await seeder.SeedAsync(CancellationToken.None);
            await seeder.SeedAsync(CancellationToken.None);
        }

        var count = Assert.Single(await _postgres.QueryAsync(
            "SELECT count(*) FROM catering.providers",
            reader => reader.GetInt64(0)));

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task The_directory_reports_the_seeded_provider_active_and_an_unknown_or_inactive_provider_not_active()
    {
        await StartAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var directory = scope.ServiceProvider.GetRequiredService<ICateringProviderDirectory>();

            Assert.True(await directory.IsActiveAsync(SeededProviderId));
            Assert.False(await directory.IsActiveAsync(UnknownProviderId));
        }

        await _postgres.ExecuteAsync(
            $"UPDATE catering.providers SET is_active = false WHERE id = '{SeededProviderId}'");

        try
        {
            // A fresh scope reads the change, because the directory uses the scoped context.
            using var scope = _factory.Services.CreateScope();
            var directory = scope.ServiceProvider.GetRequiredService<ICateringProviderDirectory>();

            Assert.False(await directory.IsActiveAsync(SeededProviderId));
        }
        finally
        {
            await _postgres.ExecuteAsync(
                $"UPDATE catering.providers SET is_active = true WHERE id = '{SeededProviderId}'");
        }
    }

    [Fact]
    public async Task A_duplicate_delivery_against_the_subscription_and_event_index_is_rejected()
    {
        await StartAsync();

        var subscriptionId = Guid.CreateVersion7();
        var eventId = Guid.CreateVersion7();

        await _postgres.ExecuteAsync(
            $"""
            INSERT INTO catering.webhook_subscriptions (id, provider_id, callback_url, is_active, created_at)
            VALUES ('{subscriptionId}', '{SeededProviderId}', 'https://rail-bites.example/hooks/smartrail', true, now())
            """);

        try
        {
            await _postgres.ExecuteAsync(DeliveryInsert(subscriptionId, eventId, Guid.CreateVersion7()));

            var exception = await Assert.ThrowsAsync<PostgresException>(
                () => _postgres.ExecuteAsync(DeliveryInsert(subscriptionId, eventId, Guid.CreateVersion7())));

            Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
        }
        finally
        {
            await _postgres.ExecuteAsync(
                $"DELETE FROM catering.webhook_deliveries WHERE subscription_id = '{subscriptionId}'");
            await _postgres.ExecuteAsync(
                $"DELETE FROM catering.webhook_subscriptions WHERE id = '{subscriptionId}'");
        }
    }

    /// <summary>One pending delivery, with the row identifier varied to isolate the failing index.</summary>
    private static string DeliveryInsert(Guid subscriptionId, Guid eventId, Guid deliveryId) =>
        $"""
        INSERT INTO catering.webhook_deliveries
            (id, subscription_id, event_id, journey_id, sequence_number, event_type, payload, status, attempt_count, next_attempt_at, created_at)
        VALUES
            ('{deliveryId}', '{subscriptionId}', '{eventId}', '{Guid.CreateVersion7()}', 1, 'delay_published', jsonb_build_object('version', 1), 'pending', 0, now(), now())
        """;

    private async Task StartAsync()
    {
        using var client = _factory.CreateApiClient();
        using var response = await client.GetAsync(HealthEndpoints.ReadyPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
