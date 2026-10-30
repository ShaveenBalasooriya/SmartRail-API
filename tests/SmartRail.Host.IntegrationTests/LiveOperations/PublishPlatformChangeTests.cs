using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartRail.Host.Http;
using SmartRail.Host.IntegrationTests.Infrastructure;
using SmartRail.RailNetwork.Contracts;
using SmartRail.RailNetwork.Infrastructure.Persistence;
using SmartRail.SharedKernel.Application.Abstractions;
using SmartRail.SharedKernel.Application.Events;

namespace SmartRail.Host.IntegrationTests.LiveOperations;

/// <summary>
/// Moving a stop's platform through <c>POST /operations/journeys/{journeyId}/platform-changes</c>
/// (status-publication spec, controller-operations task 2.5): the response, the stop showing the new
/// platform with the publish time as when it became known, the station and stop rules, the one
/// <c>platform_changed</c> event and the audit entry whose before and after are the platform
/// numbers. The station catalogue is substituted, because no network seed exists yet; the live state
/// and the audit record are real.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PublishPlatformChangeTests
{
    /// <summary>The seeded controller account (seed/identity/staff.json).</summary>
    private static readonly Guid ControllerStaffId = Guid.Parse("01a15874-e69c-70da-a689-76a7268ed330");

    /// <summary>The seeded guard account (seed/identity/staff.json).</summary>
    private static readonly Guid GuardStaffId = Guid.Parse("01a15874-e69d-73b2-b24d-d85d22fdead2");

    private readonly PostgresFixture _postgres;

    public PublishPlatformChangeTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task A_valid_platform_change_moves_the_stop_and_reaches_the_event_and_audit()
    {
        var network = new TestNetwork();
        using var factory = NewFactory(network);
        var today = RailwayToday();
        var journeyId = await CreateJourneyAsync(factory, today, network.Stops(today));
        var auditBaseline = await MaxAuditPositionAsync();

        try
        {
            using var client = factory.CreateApiClient();
            using var response = await PostPlatformChangeAsync(
                client,
                journeyId,
                stopSequence: 2,
                platformId: network.BetaTwo);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var change = await response.Content.ReadFromJsonAsync<ChangeItem>();
            Assert.NotNull(change);

            Assert.NotEqual(Guid.Empty, change.Id);
            Assert.Equal("platform_change", change.Type);
            Assert.Equal("2", change.Values["stopSequence"]);
            Assert.Equal(network.BetaOne.ToString(), change.Values["previousPlatformId"]);
            Assert.Equal(network.BetaTwo.ToString(), change.Values["platformId"]);
            Assert.Equal("Mahesh Perera", change.PublisherName);
            Assert.Equal("automatically_published", change.Provenance);
            Assert.False(change.Corrected);

            var stored = Assert.Single(await _postgres.QueryAsync(
                $"""
                SELECT id, change_type, stop_sequence, new_platform_id, previous_platform_id, published_by
                FROM operations.status_changes
                WHERE journey_id = '{journeyId}'
                """,
                reader => new StoredChange(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.GetGuid(3),
                    reader.GetGuid(4),
                    reader.GetGuid(5))));

            Assert.Equal(change.Id, stored.Id);
            Assert.Equal("platform_change", stored.Type);
            Assert.Equal(2, stored.StopSequence);
            Assert.Equal(network.BetaTwo, stored.NewPlatform);
            Assert.Equal(network.BetaOne, stored.PreviousPlatform);
            Assert.Equal(ControllerStaffId, stored.Publisher);

            // The stop shows the new platform at once, and the publish time is when it became known.
            var detail = await GetDetailAsync(client, journeyId);

            Assert.Equal("scheduled", detail.Journey.Status);
            Assert.Equal(new PlatformItem(network.BetaTwo, "3"), detail.Stops[1].Platform);

            var platformChangedAt = Assert.Single(await _postgres.QueryAsync(
                $"""
                SELECT platform_changed_at
                FROM operations.journey_stop_live_states
                WHERE journey_id = '{journeyId}' AND sequence = 2
                """,
                reader => reader.GetFieldValue<DateTimeOffset>(0)));

            Assert.True(
                (platformChangedAt - change.PublishedAt).Duration() < TimeSpan.FromMilliseconds(1),
                $"platform_changed_at {platformChangedAt} is not the publish time {change.PublishedAt}.");

            // One journey event, naming the change as its source.
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

            Assert.Equal("platform_changed", journeyEvent.Type);
            Assert.Equal(1, journeyEvent.SequenceNumber);
            Assert.Equal(change.Id, journeyEvent.SourceId);

            Assert.Equal("JourneyStateChanged", Assert.Single(await _postgres.QueryAsync(
                $"SELECT event_type FROM operations.outbox_messages WHERE ordering_key = '{journeyId}'",
                reader => reader.GetString(0))));

            // The audit entry names the numbers passengers see, not the identifiers (design D4).
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
            Assert.Equal("PublishPlatformChange", entry.Action);
            Assert.Equal("journey", entry.TargetType);
            Assert.Equal("succeeded", entry.Outcome);

            using var before = JsonDocument.Parse(entry.Before!);
            Assert.Equal("2", before.RootElement.GetProperty("platform").GetString());

            using var after = JsonDocument.Parse(entry.After!);
            Assert.Equal("3", after.RootElement.GetProperty("platform").GetString());
        }
        finally
        {
            await CleanUpJourneyAsync(journeyId);
            await ResetAuditEntriesAsync(auditBaseline);
        }
    }

    [Fact]
    public async Task A_platform_of_another_station_is_a_bad_request()
    {
        var network = new TestNetwork();
        using var factory = NewFactory(network);
        var today = RailwayToday();
        var journeyId = await CreateJourneyAsync(factory, today, network.Stops(today));

        try
        {
            using var client = factory.CreateApiClient();
            using var response = await PostPlatformChangeAsync(
                client,
                journeyId,
                stopSequence: 2,
                platformId: network.AlphaTwo);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
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
    public async Task An_unknown_platform_is_a_bad_request()
    {
        var network = new TestNetwork();
        using var factory = NewFactory(network);
        var today = RailwayToday();
        var journeyId = await CreateJourneyAsync(factory, today, network.Stops(today));

        try
        {
            using var client = factory.CreateApiClient();
            using var response = await PostPlatformChangeAsync(
                client,
                journeyId,
                stopSequence: 2,
                platformId: Guid.CreateVersion7());

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
    public async Task The_platform_the_stop_already_uses_is_a_conflict()
    {
        var network = new TestNetwork();
        using var factory = NewFactory(network);
        var today = RailwayToday();
        var journeyId = await CreateJourneyAsync(factory, today, network.Stops(today));

        try
        {
            using var client = factory.CreateApiClient();
            using var response = await PostPlatformChangeAsync(
                client,
                journeyId,
                stopSequence: 2,
                platformId: network.BetaOne);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
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
    public async Task A_stop_the_journey_passes_without_calling_is_a_bad_request()
    {
        var network = new TestNetwork();
        using var factory = NewFactory(network);
        var today = RailwayToday();
        var journeyId = await CreateJourneyAsync(factory, today, network.StopsWithoutCallingAtStopTwo(today));

        try
        {
            using var client = factory.CreateApiClient();
            using var response = await PostPlatformChangeAsync(
                client,
                journeyId,
                stopSequence: 2,
                platformId: network.BetaTwo);

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
    public async Task A_stop_that_has_already_departed_is_a_conflict()
    {
        var network = new TestNetwork();
        using var factory = NewFactory(network);
        var today = RailwayToday();
        var journeyId = await CreateJourneyAsync(factory, today, network.Stops(today));

        try
        {
            // The Guard has departed stop 2, so its platform is no longer open to change.
            await _postgres.ExecuteAsync(
                $"""
                UPDATE operations.journey_stop_live_states
                SET actual_departure = '{DepartAt(today).AddMinutes(11):O}'
                WHERE journey_id = '{journeyId}' AND sequence = 2
                """);
            await _postgres.ExecuteAsync(
                $"UPDATE operations.journey_live_states SET lifecycle_status = 'running' WHERE journey_id = '{journeyId}'");

            using var client = factory.CreateApiClient();
            using var response = await PostPlatformChangeAsync(
                client,
                journeyId,
                stopSequence: 2,
                platformId: network.BetaTwo);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
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
    public async Task Only_the_network_controller_may_change_a_platform()
    {
        var network = new TestNetwork();
        using var factory = NewFactory(network);
        var today = RailwayToday();
        var journeyId = await CreateJourneyAsync(factory, today, network.Stops(today));

        try
        {
            using var client = factory.CreateApiClient();
            using var response = await PostPlatformChangeAsync(
                client,
                journeyId,
                stopSequence: 2,
                platformId: network.BetaTwo,
                token: TestTokens.Create("guard.ishara", Roles.Guard));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Empty(await _postgres.QueryAsync(
                $"SELECT id FROM operations.status_changes WHERE journey_id = '{journeyId}'",
                reader => reader.GetGuid(0)));
        }
        finally
        {
            await CleanUpJourneyAsync(journeyId);
        }
    }

    private SmartRailApiFactory NewFactory(TestNetwork network) =>
        new(
            _postgres.ConnectionString,
            configureServices: services =>
            {
                services.RemoveAll<INetworkCatalog>();
                services.AddSingleton<INetworkCatalog>(network.Catalog);
            });

    private static DateOnly RailwayToday() => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTime(
            DateTimeOffset.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Colombo")).DateTime);

    private static DateTimeOffset DepartAt(DateOnly serviceDate) =>
        new(serviceDate.ToDateTime(new TimeOnly(2, 0)), TimeSpan.Zero);

    private static string PlatformChangesRoute(Guid journeyId) =>
        $"{ApiRoutes.BasePath}/operations/journeys/{journeyId}/platform-changes";

    private static string DetailRoute(Guid journeyId) =>
        $"{ApiRoutes.BasePath}/operations/journeys/{journeyId}";

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

    private static async Task<HttpResponseMessage> PostPlatformChangeAsync(
        HttpClient client,
        Guid journeyId,
        int stopSequence,
        Guid platformId,
        string? token = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, PlatformChangesRoute(journeyId))
        {
            Content = JsonContent.Create(new { stopSequence, platformId }),
        };

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

    private async Task<long> MaxAuditPositionAsync() =>
        Assert.Single(await _postgres.QueryAsync(
            "SELECT coalesce(max(chain_position), 0) FROM audit.entries",
            reader => reader.GetInt64(0)));

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

    /// <summary>
    /// The network the tests run against: two stations with two platforms each and a third with
    /// none, wired so stop 2 of the journey is at Beta on platform "2".
    /// </summary>
    private sealed class TestNetwork
    {
        public TestNetwork()
        {
            Catalog = new FakeNetworkCatalog(
            [
                new StationDto(
                    Alpha,
                    "ALP",
                    "Alpha",
                    IsInterchange: false,
                    [new PlatformDto(AlphaOne, Alpha, "1"), new PlatformDto(AlphaTwo, Alpha, "2")],
                    []),
                new StationDto(
                    Beta,
                    "BET",
                    "Beta",
                    IsInterchange: false,
                    [new PlatformDto(BetaOne, Beta, "2"), new PlatformDto(BetaTwo, Beta, "3")],
                    []),
                new StationDto(Gamma, "GAM", "Gamma", IsInterchange: false, [], []),
            ]);
        }

        public Guid Alpha { get; } = Guid.CreateVersion7();

        public Guid AlphaOne { get; } = Guid.CreateVersion7();

        public Guid AlphaTwo { get; } = Guid.CreateVersion7();

        public Guid Beta { get; } = Guid.CreateVersion7();

        public Guid BetaOne { get; } = Guid.CreateVersion7();

        public Guid BetaTwo { get; } = Guid.CreateVersion7();

        public Guid Gamma { get; } = Guid.CreateVersion7();

        public FakeNetworkCatalog Catalog { get; }

        /// <summary>Three calling stops; stop 2 is at Beta on platform "2".</summary>
        public IReadOnlyList<JourneyStopDto> Stops(DateOnly serviceDate)
        {
            var departAt = DepartAt(serviceDate);

            return
            [
                new JourneyStopDto(1, Alpha, true, null, departAt, AlphaOne),
                new JourneyStopDto(2, Beta, true, departAt.AddMinutes(10), departAt.AddMinutes(11), BetaOne),
                new JourneyStopDto(3, Gamma, true, departAt.AddMinutes(20), null, null),
            ];
        }

        /// <summary>The same journey with stop 2 passed without calling at it.</summary>
        public IReadOnlyList<JourneyStopDto> StopsWithoutCallingAtStopTwo(DateOnly serviceDate)
        {
            var departAt = DepartAt(serviceDate);

            return
            [
                new JourneyStopDto(1, Alpha, true, null, departAt, AlphaOne),
                new JourneyStopDto(2, Beta, false, null, null, null),
                new JourneyStopDto(3, Gamma, true, departAt.AddMinutes(20), null, null),
            ];
        }
    }

    /// <summary>The station catalogue, substituted because the Rail Network holds no seed yet.</summary>
    private sealed class FakeNetworkCatalog(IReadOnlyList<StationDto> stations) : INetworkCatalog
    {
        public Task<IReadOnlyList<LineDto>> GetLinesAsync(
            ServiceDirection direction = ServiceDirection.Outbound,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LineDto>>([]);

        public Task<IReadOnlyList<StationDto>> GetStationsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(stations);

        public Task<StationDto?> GetStationAsync(Guid stationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(stations.FirstOrDefault(station => station.Id == stationId));

        public Task<IReadOnlyList<PlatformDto>> GetPlatformsAsync(
            Guid stationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PlatformDto>>(
                stations.FirstOrDefault(station => station.Id == stationId)?.Platforms ?? []);

        public Task<bool> IsInterchangeAsync(Guid stationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private sealed record StoredChange(
        Guid Id,
        string Type,
        int StopSequence,
        Guid NewPlatform,
        Guid PreviousPlatform,
        Guid Publisher);

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

    private sealed record PlatformItem(Guid Id, string Number);

    private sealed record StopItem(int Sequence, bool IsCalling, PlatformItem? Platform);

    private sealed record JourneyItem(string Status);

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
}
