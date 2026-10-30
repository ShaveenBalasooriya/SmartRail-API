using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartRail.Host.Http;
using SmartRail.Host.IntegrationTests.Authorization;
using SmartRail.Host.IntegrationTests.Infrastructure;
using SmartRail.RailNetwork.Contracts;
using SmartRail.RailNetwork.Infrastructure.Persistence;
using SmartRail.SharedKernel.Application.Abstractions;
using SmartRail.SharedKernel.Application.Events;

namespace SmartRail.Host.IntegrationTests.Catering;

/// <summary>
/// The provider read API against a real database (tasks 1.2 and 1.3; provider-data-api spec): the
/// nine seeded stations with their platforms, the unknown station, the journeys calling at a station
/// ordered by expected arrival with a terminating journey included, the date beyond tomorrow, a
/// response carrying no staff or guard field, and the key that is required. The Host runs with a
/// fixed clock at noon in the railway's time zone, so the seeded network and timetable are the same
/// whenever the suite runs.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ProviderDataApiTests
{
    /// <summary>
    /// The key the tests supply through configuration; it is the same value the other provider tests
    /// use, so the shared database keeps exactly one provider key row whichever test starts first.
    /// </summary>
    private const string PlaintextProviderKey = "srk_ab12cd34_5f2c8d7e9b1a4c6d3e0f7a2b8c9d1e4f";

    /// <summary>The setting the seeder reads the plaintext key from.</summary>
    private const string ProviderKeySetting = "Catering:ProviderApiKey";

    /// <summary>The seeded Coastal Line (seed/network/network.json).</summary>
    private static readonly Guid CoastalLine = Guid.Parse("f8493f1c-67b5-76e1-acb6-215d4d9f60ae");

    /// <summary>A seeded outbound Coastal Line service, which gives a generated journey a direction.</summary>
    private static readonly Guid OutboundService = Guid.Parse("d84562ca-6351-7a4a-90cd-fb7e45f4f0e9");

    /// <summary>The seeded guard account, which owns every seeded service.</summary>
    private static readonly Guid GuardStaffId = Guid.Parse("01a15874-e69d-73b2-b24d-d85d22fdead2");

    private static readonly Guid ColomboFort = Guid.Parse("04624d0f-9410-72e3-967f-501ed71f2441");
    private static readonly Guid Kollupitiya = Guid.Parse("dc1ed19b-f522-70a9-ae61-15e47102a42f");
    private static readonly Guid MountLavinia = Guid.Parse("c078a9aa-2f80-7422-a65b-bb7313795bd5");

    private static readonly Guid ColomboFortPlatformTwo = Guid.Parse("61e67fba-522e-73bf-beab-5cb90e861c86");
    private static readonly Guid KollupitiyaPlatform = Guid.Parse("e86062e1-aa04-7673-b2dc-51892f077a58");
    private static readonly Guid MountLaviniaPlatform = Guid.Parse("579f8b1e-e3fe-789b-9ba3-20aae4342df3");

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private readonly PostgresFixture _postgres;

    public ProviderDataApiTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task The_station_list_returns_the_nine_seeded_stations_with_their_platforms_and_lines()
    {
        using var factory = NewFactory(SafeNow(RailwayToday()));
        using var client = factory.CreateApiClient();

        using var response = await SendAsync(client, StationsRoute());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var stations = document.RootElement.EnumerateArray().ToArray();

        Assert.Equal(9, stations.Length);
        Assert.All(stations, station => Assert.NotEmpty(station.GetProperty("platforms").EnumerateArray()));
        Assert.All(stations, station => Assert.NotEmpty(station.GetProperty("lines").EnumerateArray()));

        var colomboFort = Assert.Single(
            stations,
            station => station.GetProperty("id").GetGuid() == ColomboFort);

        Assert.Equal("Colombo Fort", colomboFort.GetProperty("name").GetString());
        Assert.True(colomboFort.GetProperty("isInterchange").GetBoolean());
        Assert.Equal(3, colomboFort.GetProperty("platforms").GetArrayLength());
        Assert.Equal(2, colomboFort.GetProperty("lines").GetArrayLength());

        var kollupitiya = Assert.Single(
            stations,
            station => station.GetProperty("id").GetGuid() == Kollupitiya);

        var platform = Assert.Single(kollupitiya.GetProperty("platforms").EnumerateArray());
        Assert.Equal(KollupitiyaPlatform, platform.GetProperty("id").GetGuid());
        Assert.Equal("1", platform.GetProperty("number").GetString());
    }

    [Fact]
    public async Task An_unknown_station_is_not_found()
    {
        using var factory = NewFactory(SafeNow(RailwayToday()));
        using var client = factory.CreateApiClient();

        using var response = await SendAsync(client, StationRoute(Guid.CreateVersion7()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task The_journeys_at_a_station_are_ordered_by_expected_arrival_and_include_a_terminating_one()
    {
        var today = RailwayToday();
        var now = SafeNow(today);
        using var factory = NewFactory(now);

        // Calls at Kollupitiya on its way to Mount Lavinia.
        var passing = await CreateJourneyAsync(
            factory,
            today,
            [
                Stop(1, ColomboFort, ColomboFortPlatformTwo, null, now.AddMinutes(30)),
                Stop(2, Kollupitiya, KollupitiyaPlatform, now.AddMinutes(40), now.AddMinutes(41)),
                Stop(3, MountLavinia, MountLaviniaPlatform, now.AddMinutes(55), null),
            ]);

        // Ends at Kollupitiya: it is listed with its arrival and no departure.
        var terminating = await CreateJourneyAsync(
            factory,
            today,
            [
                Stop(1, ColomboFort, ColomboFortPlatformTwo, null, now.AddMinutes(10)),
                Stop(2, Kollupitiya, KollupitiyaPlatform, now.AddMinutes(20), null),
            ]);

        try
        {
            using var client = factory.CreateApiClient();

            using var response = await SendAsync(client, JourneysRoute(Kollupitiya, today));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var items = document.RootElement.EnumerateArray().ToArray();

            var arriving = Assert.Single(
                items,
                item => item.GetProperty("journeyId").GetGuid() == passing);

            Assert.Equal(now.AddMinutes(40), arriving.GetProperty("scheduledArrival").GetDateTimeOffset());
            Assert.Equal(now.AddMinutes(40), arriving.GetProperty("expectedArrival").GetDateTimeOffset());
            Assert.Equal("Coastal Line", arriving.GetProperty("line").GetProperty("name").GetString());
            Assert.Equal("outbound", arriving.GetProperty("direction").GetString());
            Assert.Equal(
                MountLavinia,
                arriving.GetProperty("finalDestination").GetProperty("id").GetGuid());
            Assert.Equal(
                "Mount Lavinia",
                arriving.GetProperty("finalDestination").GetProperty("name").GetString());
            Assert.Equal("1", arriving.GetProperty("platform").GetProperty("number").GetString());
            Assert.Equal("scheduled", arriving.GetProperty("status").GetString());
            Assert.Equal(0, arriving.GetProperty("sequenceNumber").GetInt64());
            Assert.Equal(now, arriving.GetProperty("lastUpdatedAt").GetDateTimeOffset());

            var last = Assert.Single(
                items,
                item => item.GetProperty("journeyId").GetGuid() == terminating);

            Assert.Equal(now.AddMinutes(20), last.GetProperty("scheduledArrival").GetDateTimeOffset());
            Assert.Equal(now.AddMinutes(20), last.GetProperty("expectedArrival").GetDateTimeOffset());
            Assert.Equal(Kollupitiya, last.GetProperty("finalDestination").GetProperty("id").GetGuid());
            Assert.False(last.TryGetProperty("scheduledDeparture", out _), "a terminating journey has no departure.");
            Assert.False(last.TryGetProperty("expectedDeparture", out _), "a terminating journey has no departure.");

            // The whole list is in expected-arrival order, and the earlier terminating journey comes
            // before the later passing one.
            var arrivals = items
                .Where(item => item.TryGetProperty("expectedArrival", out var arrival)
                    && arrival.ValueKind is not JsonValueKind.Null)
                .Select(item => item.GetProperty("expectedArrival").GetDateTimeOffset())
                .ToArray();

            Assert.Equal(arrivals.OrderBy(arrival => arrival).ToArray(), arrivals);

            var order = items.Select(item => item.GetProperty("journeyId").GetGuid()).ToArray();
            Assert.True(
                Array.IndexOf(order, terminating) < Array.IndexOf(order, passing),
                "the journey arriving first must be listed first.");
        }
        finally
        {
            await CleanUpAsync([passing, terminating]);
        }
    }

    [Fact]
    public async Task A_date_beyond_tomorrow_is_a_validation_problem()
    {
        using var factory = NewFactory(SafeNow(RailwayToday()));
        using var client = factory.CreateApiClient();

        using var response = await SendAsync(client, JourneysRoute(Kollupitiya, RailwayToday().AddDays(2)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("errors").TryGetProperty("date", out _));
    }

    [Fact]
    public async Task No_provider_response_carries_a_guard_or_staff_field()
    {
        var today = RailwayToday();
        using var factory = NewFactory(SafeNow(today));
        using var client = factory.CreateApiClient();

        var routes = new[]
        {
            StationsRoute(),
            StationRoute(Kollupitiya),
            JourneysRoute(Kollupitiya, today),
        };

        foreach (var route in routes)
        {
            using var response = await SendAsync(client, route);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();

            Assert.DoesNotContain("guard", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("staff", body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task A_request_without_a_provider_key_is_unauthorized()
    {
        using var factory = NewFactory(SafeNow(RailwayToday()));
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync(StationsRoute());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private SmartRailApiFactory NewFactory(DateTimeOffset now) =>
        new(
            _postgres.ConnectionString,
            settings: new Dictionary<string, string>
            {
                [ProviderKeySetting] = PlaintextProviderKey,
            },
            configureServices: services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(now));
            });

    private static string StationsRoute() => $"{ApiRoutes.BasePath}/provider/stations";

    private static string StationRoute(Guid stationId) => $"{StationsRoute()}/{stationId}";

    private static string JourneysRoute(Guid stationId, DateOnly date) =>
        $"{StationRoute(stationId)}/journeys?date={date:yyyy-MM-dd}";

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string route)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, route).WithApiKey(PlaintextProviderKey);

        return await client.SendAsync(request);
    }

    /// <summary>Creates a journey's live state through the real generation announcement.</summary>
    private async Task<Guid> CreateJourneyAsync(
        SmartRailApiFactory factory,
        DateOnly serviceDate,
        IReadOnlyList<JourneyStopDto> stops)
    {
        using var client = factory.CreateApiClient();
        using var health = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        var journeyId = Guid.CreateVersion7();

        using var scope = factory.Services.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredKeyedService<IIntegrationEventPublisher>(
            IntegrationEventPublisherKeys.Network);
        var database = scope.ServiceProvider.GetRequiredService<RailNetworkDbContext>();

        publisher.Publish(
            new JourneyGenerated(
                journeyId,
                OutboundService,
                serviceDate,
                CoastalLine,
                ServiceDirection.Outbound,
                GuardStaffId,
                stops),
            journeyId);

        await database.SaveChangesAsync();

        var deadline = DateTimeOffset.UtcNow.Add(Timeout);
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

    private static JourneyStopDto Stop(
        int sequence,
        Guid stationId,
        Guid? platformId,
        DateTimeOffset? arrival,
        DateTimeOffset? departure) =>
        new(sequence, stationId, true, arrival, departure, platformId);

    private async Task<int> CountAsync(string sql)
    {
        var count = Assert.Single(await _postgres.QueryAsync(sql, reader => reader.GetInt64(0)));
        return (int)count;
    }

    /// <summary>Removes every row the test created for its journeys.</summary>
    private async Task CleanUpAsync(IReadOnlyList<Guid> journeyIds)
    {
        foreach (var journeyId in journeyIds)
        {
            await _postgres.ExecuteAsync(
                $"DELETE FROM operations.departure_logs WHERE journey_id = '{journeyId}'");
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
            await _postgres.ExecuteAsync(
                $"DELETE FROM network.outbox_deliveries WHERE message_id = '{journeyId}'");
            await _postgres.ExecuteAsync(
                $"DELETE FROM network.outbox_messages WHERE id = '{journeyId}'");
        }
    }

    private static DateOnly RailwayToday() => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTime(
            DateTimeOffset.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Colombo")).DateTime);

    /// <summary>
    /// Noon in the railway's time zone on the given day, as UTC: safely inside the day, so the
    /// seeded timetable and its departures around it are the same whenever the suite runs.
    /// </summary>
    private static DateTimeOffset SafeNow(DateOnly serviceDate) =>
        new DateTimeOffset(
            serviceDate.ToDateTime(new TimeOnly(12, 0)),
            TimeSpan.FromHours(5.5)).ToUniversalTime();

    /// <summary>A clock frozen at one instant, so "now" and the seeded timetable line up exactly.</summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
