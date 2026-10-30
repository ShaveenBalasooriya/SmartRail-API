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
/// The Guard's journey views over HTTP: the role policy, the ownership filter that answers another
/// guard's journey as not found, the empty list, the departure order, the calling stops with their
/// logged and final flags, and the published changes a controller makes reaching the guard's detail.
/// The timetable and station catalogue are substituted, because no network seed exists yet; the
/// live state, the published changes and the audit record are real.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class GuardViewTests : IDisposable
{
    /// <summary>The seeded guard account (seed/identity/staff.json).</summary>
    private static readonly Guid GuardStaffId = Guid.Parse("01a15874-e69d-73b2-b24d-d85d22fdead2");

    /// <summary>The seeded controller account (seed/identity/staff.json).</summary>
    private static readonly Guid ControllerStaffId = Guid.Parse("01a15874-e69c-70da-a689-76a7268ed330");

    /// <summary>
    /// The staff account the second guard's journeys are assigned to (seed/identity/staff.json).
    /// The seed has one guard account, so the second guard is another seeded staff id: a token's
    /// staff account comes from its subject, and its role from the group it names, so a token for
    /// this subject holding the guard group is a guard with a different staff id.
    /// </summary>
    private static readonly Guid SecondGuardStaffId = Guid.Parse("01a15874-e6a0-7fd3-8c46-34bd36e8f16d");

    /// <summary>The subject of the second guard's token.</summary>
    private const string SecondGuardSubject = "auditor.wasantha";

    private const string GuardSubject = "guard.ishara";

    private const string ControllerSubject = "controller.mahesh";

    private static readonly Guid StationAlpha = Guid.CreateVersion7();
    private static readonly Guid StationBeta = Guid.CreateVersion7();
    private static readonly Guid StationGamma = Guid.CreateVersion7();
    private static readonly Guid PlatformAlphaOne = Guid.CreateVersion7();
    private static readonly Guid PlatformBetaOne = Guid.CreateVersion7();
    private static readonly Guid PlatformBetaTwo = Guid.CreateVersion7();

    private static readonly string JourneysRoute = $"{ApiRoutes.BasePath}/guard/journeys";

    /// <summary>The web defaults the API writes JSON with, for the one assertion that reads the raw body.</summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly PostgresFixture _postgres;
    private readonly FakeTimetable _timetable = new();
    private readonly FakeNetworkCatalog _network = new();
    private readonly SmartRailApiFactory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _journeys = [];

    public GuardViewTests(PostgresFixture postgres)
    {
        _postgres = postgres;
        _network.Stations =
        [
            new StationDto(
                StationAlpha,
                "ALP",
                "Alpha",
                IsInterchange: false,
                [new PlatformDto(PlatformAlphaOne, StationAlpha, "1")],
                []),
            new StationDto(
                StationBeta,
                "BET",
                "Beta",
                IsInterchange: false,
                [new PlatformDto(PlatformBetaOne, StationBeta, "2"), new PlatformDto(PlatformBetaTwo, StationBeta, "3")],
                []),
            new StationDto(StationGamma, "GAM", "Gamma", IsInterchange: false, [], []),
        ];
        _factory = new SmartRailApiFactory(
            postgres.ConnectionString,
            configureServices: services =>
            {
                services.RemoveAll<ITimetable>();
                services.AddSingleton<ITimetable>(_timetable);
                services.RemoveAll<INetworkCatalog>();
                services.AddSingleton<INetworkCatalog>(_network);
            });
        _client = _factory.CreateApiClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Theory]
    [InlineData(ControllerSubject, Roles.NetworkController)]
    [InlineData("auditor.wasantha", Roles.Auditor)]
    public async Task Only_a_guard_may_read_the_guard_views(string subject, string role)
    {
        var token = TestTokens.Create(subject, role);
        var journeyId = Guid.CreateVersion7();

        using var list = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, JourneysRoute).WithStaffToken(token));
        using var detail = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, DetailRoute(journeyId)).WithStaffToken(token));

        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, detail.StatusCode);
        Assert.Equal("application/problem+json", list.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_request_with_no_token_is_unauthorized()
    {
        using var list = await _client.GetAsync(JourneysRoute);
        using var detail = await _client.GetAsync(DetailRoute(Guid.CreateVersion7()));

        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, detail.StatusCode);
    }

    [Fact]
    public async Task A_guard_with_no_journeys_today_gets_an_empty_list()
    {
        await StartAsync();

        using var response = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, JourneysRoute)
                .WithStaffToken(TestTokens.Create(SecondGuardSubject, Roles.Guard)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var listed = await response.Content.ReadFromJsonAsync<List<JourneyListItem>>();
        Assert.NotNull(listed);
        Assert.Empty(listed);
    }

    [Fact]
    public async Task Two_guards_each_see_only_their_own_journeys()
    {
        await StartAsync();

        var today = RailwayToday();
        var guard = TestTokens.Create(GuardSubject, Roles.Guard);
        var secondGuard = TestTokens.Create(SecondGuardSubject, Roles.Guard);

        try
        {
            var ownDeparture = DepartAt(today, 2);
            var own = await PublishJourneyAsync(today, GuardStaffId, "Kandy Express", ownDeparture, Stops(ownDeparture));

            var theirDeparture = DepartAt(today, 3);
            var theirs = await PublishJourneyAsync(today, SecondGuardStaffId, "Ruhunu Express", theirDeparture, Stops(theirDeparture));

            // A journey of another service date is never in today's list, but its detail is served,
            // because a queued sync may arrive after midnight.
            var yesterday = today.AddDays(-1);
            var earlierDeparture = DepartAt(yesterday, 4);
            var earlier = await PublishJourneyAsync(yesterday, GuardStaffId, "Night Mail", earlierDeparture, Stops(earlierDeparture));

            var ownList = await ListAsync(guard);
            Assert.Contains(ownList, item => item.Id == own);
            Assert.DoesNotContain(ownList, item => item.Id == theirs);
            Assert.DoesNotContain(ownList, item => item.Id == earlier);

            var theirList = await ListAsync(secondGuard);
            Assert.Contains(theirList, item => item.Id == theirs);
            Assert.DoesNotContain(theirList, item => item.Id == own);

            // Another guard's journey is not found, never forbidden: the ID reveals nothing.
            Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(DetailRoute(theirs), guard));
            Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(DetailRoute(own), secondGuard));

            Assert.Equal(HttpStatusCode.OK, await StatusAsync(DetailRoute(earlier), guard));
        }
        finally
        {
            await CleanUpAsync();
        }
    }

    [Fact]
    public async Task Today_lists_the_guards_journeys_in_departure_order_with_only_their_calling_stops()
    {
        await StartAsync();

        var today = RailwayToday();
        var guard = TestTokens.Create(GuardSubject, Roles.Guard);

        try
        {
            var lateDeparture = DepartAt(today, 6);
            var lateStops = new JourneyStopDto[]
            {
                new(1, StationAlpha, true, null, lateDeparture, PlatformAlphaOne),
                new(2, StationBeta, false, null, null, null),
                new(3, StationGamma, true, lateDeparture.AddMinutes(20), null, null),
            };
            var late = await PublishJourneyAsync(today, GuardStaffId, "Kandy Express", lateDeparture, lateStops);

            var earlyDeparture = DepartAt(today, 2);
            var early = await PublishJourneyAsync(today, GuardStaffId, "Ruhunu Express", earlyDeparture, Stops(earlyDeparture));

            // The guard has already logged the late journey's first departure.
            await _postgres.ExecuteAsync(
                $"""
                UPDATE operations.journey_stop_live_states
                SET actual_departure = '{lateDeparture.AddMinutes(1):O}'
                WHERE journey_id = '{late}' AND sequence = 1
                """);
            await _postgres.ExecuteAsync(
                $"UPDATE operations.journey_live_states SET lifecycle_status = 'running' WHERE journey_id = '{late}'");

            var listed = await ListAsync(guard);

            // Only this test's journeys matter here; another test's journey may share the guard.
            Assert.Equal(
                new[] { early, late },
                listed
                    .Where(item => item.Id == early || item.Id == late)
                    .Select(item => item.Id));

            var item = Assert.Single(listed, candidate => candidate.Id == late);
            Assert.Equal("Kandy Express", item.ServiceName);
            Assert.Equal("L1", item.LineCode);
            Assert.Equal("Main Line", item.LineName);
            Assert.Equal("outbound", item.Direction);
            Assert.Equal("running", item.Status);

            // Only the calling stops are listed, and the last calling stop is the final one.
            Assert.Equal(new[] { 1, 3 }, item.Stops.Select(stop => stop.Sequence));
            Assert.Equal(new NamedStation(StationAlpha, "Alpha"), item.Stops[0].Station);
            Assert.Equal(new PlatformItem(PlatformAlphaOne, "1"), item.Stops[0].Platform);
            Assert.True(item.Stops[0].DepartureLogged);
            Assert.False(item.Stops[0].IsFinalStop);
            Assert.Null(item.Stops[1].Platform);
            Assert.False(item.Stops[1].DepartureLogged);
            Assert.True(item.Stops[1].IsFinalStop);
        }
        finally
        {
            await CleanUpAsync();
        }
    }

    [Fact]
    public async Task The_detail_shows_the_guards_journey_and_the_changes_published_on_it()
    {
        await StartAsync();

        var today = RailwayToday();
        var guard = TestTokens.Create(GuardSubject, Roles.Guard);
        var auditBaseline = await MaxAuditPositionAsync();

        try
        {
            var departure = DepartAt(today, 2);
            var journeyId = await PublishJourneyAsync(today, GuardStaffId, "Kandy Express", departure, Stops(departure));

            var platformChange = await PublishPlatformChangeAsync(journeyId, stopSequence: 2, PlatformBetaTwo);
            var delay = await PublishDelayAsync(journeyId, fromStopSequence: 1, minutes: 5);

            using var response = await _client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, DetailRoute(journeyId)).WithStaffToken(guard));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            var detail = JsonSerializer.Deserialize<JourneyDetail>(body, JsonOptions);

            Assert.NotNull(detail);
            Assert.Equal(journeyId, detail.Journey.Id);
            Assert.Equal("Kandy Express", detail.Journey.ServiceName);
            Assert.Equal(new[] { 1, 2, 3 }, detail.Journey.Stops.Select(stop => stop.Sequence));

            // The controller's platform change reached the stop, with the time it was published.
            var moved = detail.Journey.Stops[1];
            Assert.Equal(new PlatformItem(PlatformBetaTwo, "3"), moved.Platform);
            Assert.NotNull(moved.PlatformChangedAt);
            Assert.True(
                (moved.PlatformChangedAt.Value - platformChange.PublishedAt).Duration() < TimeSpan.FromMilliseconds(1),
                $"platformChangedAt {moved.PlatformChangedAt} is not the publish time {platformChange.PublishedAt}.");

            // The delay reached the expected times of the stop it starts at and every later stop.
            Assert.Equal(5, detail.Journey.DelayMinutes);
            Assert.Equal(departure.AddMinutes(11).AddMinutes(5), moved.ExpectedDeparture);
            Assert.Equal(departure.AddMinutes(20).AddMinutes(5), detail.Journey.Stops[2].ExpectedArrival);

            Assert.Equal(2, detail.Changes.Count);

            var published = Assert.Single(detail.Changes, change => change.Type == "platform_change");
            Assert.Equal(platformChange.Id, published.Id);
            Assert.Equal("2", published.Values["stopSequence"]);
            Assert.Equal(PlatformBetaTwo.ToString(), published.Values["platformId"]);
            Assert.False(published.Corrected);
            Assert.True((published.PublishedAt - platformChange.PublishedAt).Duration() < TimeSpan.FromMilliseconds(1));

            var delayed = Assert.Single(detail.Changes, change => change.Type == "delay");
            Assert.Equal(delay.Id, delayed.Id);
            Assert.Equal("1", delayed.Values["fromStopSequence"]);
            Assert.Equal("5", delayed.Values["minutes"]);
            Assert.True((delayed.PublishedAt - delay.PublishedAt).Duration() < TimeSpan.FromMilliseconds(1));

            // The guard needs neither the publisher nor any staff identifier.
            Assert.DoesNotContain("publisher", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(ControllerStaffId.ToString(), body, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await CleanUpAsync();
            await ResetAuditEntriesAsync(auditBaseline);
        }
    }

    [Fact]
    public async Task An_unknown_journey_is_not_found()
    {
        await StartAsync();

        using var response = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, DetailRoute(Guid.CreateVersion7()))
                .WithStaffToken(TestTokens.Create(GuardSubject, Roles.Guard)));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private static string DetailRoute(Guid journeyId) => $"{JourneysRoute}/{journeyId}";

    private static DateOnly RailwayToday() => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTime(
            DateTimeOffset.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Colombo")).DateTime);

    private static DateTimeOffset DepartAt(DateOnly serviceDate, int hour) =>
        new(serviceDate.ToDateTime(new TimeOnly(hour, 0)), TimeSpan.Zero);

    /// <summary>Three calling stops, the second at Beta on platform "2".</summary>
    private static IReadOnlyList<JourneyStopDto> Stops(DateTimeOffset departure) =>
    [
        new JourneyStopDto(1, StationAlpha, true, null, departure, PlatformAlphaOne),
        new JourneyStopDto(2, StationBeta, true, departure.AddMinutes(10), departure.AddMinutes(11), PlatformBetaOne),
        new JourneyStopDto(3, StationGamma, true, departure.AddMinutes(20), null, null),
    ];

    /// <summary>Starts the Host, so migrations and seeding have run before a test publishes anything.</summary>
    private async Task StartAsync()
    {
        using var response = await _client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<List<JourneyListItem>> ListAsync(string token)
    {
        using var response = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, JourneysRoute).WithStaffToken(token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var listed = await response.Content.ReadFromJsonAsync<List<JourneyListItem>>();
        Assert.NotNull(listed);
        return listed;
    }

    private async Task<HttpStatusCode> StatusAsync(string route, string token)
    {
        using var response = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, route).WithStaffToken(token));

        return response.StatusCode;
    }

    private async Task<ChangeItem> PublishPlatformChangeAsync(Guid journeyId, int stopSequence, Guid platformId)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{ApiRoutes.BasePath}/operations/journeys/{journeyId}/platform-changes")
        {
            Content = JsonContent.Create(new { stopSequence, platformId }),
        };
        request.WithStaffToken(TestTokens.Create(ControllerSubject, Roles.NetworkController));

        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var change = await response.Content.ReadFromJsonAsync<ChangeItem>();
        Assert.NotNull(change);
        return change;
    }

    private async Task<ChangeItem> PublishDelayAsync(Guid journeyId, int fromStopSequence, int minutes)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{ApiRoutes.BasePath}/operations/journeys/{journeyId}/delays")
        {
            Content = JsonContent.Create(new { fromStopSequence, minutes }),
        };
        request.WithStaffToken(TestTokens.Create(ControllerSubject, Roles.NetworkController));

        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var change = await response.Content.ReadFromJsonAsync<ChangeItem>();
        Assert.NotNull(change);
        return change;
    }

    private async Task<Guid> PublishJourneyAsync(
        DateOnly serviceDate,
        Guid guardStaffId,
        string serviceName,
        DateTimeOffset departure,
        IReadOnlyList<JourneyStopDto> stops)
    {
        var journeyId = Guid.CreateVersion7();
        var serviceId = Guid.CreateVersion7();
        var lineId = Guid.CreateVersion7();

        _timetable.Journeys.Add(new JourneyDto(
            journeyId,
            serviceId,
            serviceName,
            lineId,
            "L1",
            "Main Line",
            ServiceDirection.Outbound,
            serviceDate,
            guardStaffId,
            departure,
            stops));

        using var scope = _factory.Services.CreateScope();

        // Rail Network announces a generated journey through its outbox; the Live Operations
        // consumer then creates the live state, so the test waits for that before reading.
        var publisher = scope.ServiceProvider.GetRequiredKeyedService<IIntegrationEventPublisher>(
            IntegrationEventPublisherKeys.Network);
        var database = scope.ServiceProvider.GetRequiredService<RailNetworkDbContext>();

        publisher.Publish(
            new JourneyGenerated(
                journeyId,
                serviceId,
                serviceDate,
                lineId,
                ServiceDirection.Outbound,
                guardStaffId,
                stops),
            journeyId);

        await database.SaveChangesAsync();

        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (await LiveStateCountAsync(journeyId) == 0 && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        Assert.Equal(1, await LiveStateCountAsync(journeyId));

        _journeys.Add(journeyId);
        return journeyId;
    }

    private async Task<int> LiveStateCountAsync(Guid journeyId)
    {
        var count = Assert.Single(await _postgres.QueryAsync(
            $"SELECT count(*) FROM operations.journey_live_states WHERE journey_id = '{journeyId}'",
            reader => reader.GetInt64(0)));

        return (int)count;
    }

    private async Task<long> MaxAuditPositionAsync() =>
        Assert.Single(await _postgres.QueryAsync(
            "SELECT coalesce(max(chain_position), 0) FROM audit.entries",
            reader => reader.GetInt64(0)));

    /// <summary>
    /// Removes every row the test created for its journeys: the change first (self reference), then
    /// the event, its outbox row, the stops and the state.
    /// </summary>
    private async Task CleanUpAsync()
    {
        foreach (var journeyId in _journeys)
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

        _journeys.Clear();
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

    private sealed record NamedStation(Guid Id, string Name);

    private sealed record PlatformItem(Guid Id, string Number);

    private sealed record StopItem(
        int Sequence,
        NamedStation? Station,
        DateTimeOffset? ScheduledArrival,
        DateTimeOffset? ScheduledDeparture,
        DateTimeOffset? ExpectedArrival,
        DateTimeOffset? ExpectedDeparture,
        DateTimeOffset? ActualDeparture,
        PlatformItem? Platform,
        DateTimeOffset? PlatformChangedAt,
        bool DepartureLogged,
        bool IsFinalStop);

    private sealed record JourneyListItem(
        Guid Id,
        string? ServiceName,
        string? LineCode,
        string? LineName,
        string? Direction,
        string Status,
        int? DelayMinutes,
        DateTimeOffset LastUpdatedAt,
        IReadOnlyList<StopItem> Stops);

    private sealed record ChangeItem(
        Guid Id,
        string Type,
        IReadOnlyDictionary<string, string?> Values,
        DateTimeOffset PublishedAt,
        bool Corrected);

    private sealed record JourneyDetail(
        JourneyListItem Journey,
        IReadOnlyList<ChangeItem> Changes);

    /// <summary>The timetable's journeys for a day, substituted because the Rail Network holds no seed yet.</summary>
    private sealed class FakeTimetable : ITimetable
    {
        public List<JourneyDto> Journeys { get; } = [];

        public Task<ServiceDto?> GetServiceAsync(Guid serviceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ServiceDto?>(null);

        public Task<IReadOnlyList<JourneyDto>> GetScheduledJourneysAsync(
            DateOnly serviceDate,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<JourneyDto>>([.. Journeys.Where(journey => journey.ServiceDate == serviceDate)]);
    }

    /// <summary>The station catalogue, substituted because the Rail Network holds no seed yet.</summary>
    private sealed class FakeNetworkCatalog : INetworkCatalog
    {
        public IReadOnlyList<StationDto> Stations { get; set; } = [];

        public Task<IReadOnlyList<LineDto>> GetLinesAsync(
            ServiceDirection direction = ServiceDirection.Outbound,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LineDto>>([]);

        public Task<IReadOnlyList<StationDto>> GetStationsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Stations);

        public Task<StationDto?> GetStationAsync(Guid stationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Stations.FirstOrDefault(station => station.Id == stationId));

        public Task<IReadOnlyList<PlatformDto>> GetPlatformsAsync(Guid stationId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PlatformDto>>(Stations.FirstOrDefault(station => station.Id == stationId)?.Platforms ?? []);

        public Task<bool> IsInterchangeAsync(Guid stationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
