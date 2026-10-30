using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartRail.Host.Http;
using SmartRail.Host.IntegrationTests.Infrastructure;
using SmartRail.LiveOperations.Application.ControllerView;
using SmartRail.RailNetwork.Contracts;
using SmartRail.RailNetwork.Infrastructure.Persistence;
using SmartRail.SharedKernel.Application.Events;
using SmartRail.SharedKernel.Application.Abstractions;

namespace SmartRail.Host.IntegrationTests.LiveOperations;

/// <summary>
/// The Network Controller's live view over HTTP: the role policy, the 400 and 404 answers, and the
/// shape of today's journeys and one journey's detail. The timetable and station catalogue are
/// substituted, because no network seed exists yet; staff names come from the real seeded Identity
/// directory, so the guard and controller are named exactly as the seed names them.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ControllerViewTests : IDisposable
{
    /// <summary>The seeded controller account (seed/identity/staff.json).</summary>
    private const string ControllerStaffId = "01a15874-e69c-70da-a689-76a7268ed330";

    /// <summary>The seeded guard account (seed/identity/staff.json).</summary>
    private const string GuardStaffId = "01a15874-e69d-73b2-b24d-d85d22fdead2";

    /// <summary>The display name the seed gives the guard account.</summary>
    private const string GuardName = "Ishara Fernando";

    private const string JourneysRoute = $"{ApiRoutes.BasePath}/operations/journeys";

    private static readonly Guid StationAlpha = Guid.CreateVersion7();
    private static readonly Guid StationBeta = Guid.CreateVersion7();
    private static readonly Guid StationGamma = Guid.CreateVersion7();
    private static readonly Guid PlatformOne = Guid.CreateVersion7();
    private static readonly Guid PlatformTwo = Guid.CreateVersion7();

    private readonly PostgresFixture _postgres;
    private readonly FakeTimetable _timetable = new();
    private readonly FakeNetworkCatalog _network = new();
    private readonly FakeStatusChangeReader _changes = new();
    private readonly SmartRailApiFactory _factory;
    private readonly HttpClient _client;

    public ControllerViewTests(PostgresFixture postgres)
    {
        _postgres = postgres;
        _factory = new SmartRailApiFactory(
            postgres.ConnectionString,
            configureServices: services =>
            {
                services.RemoveAll<ITimetable>();
                services.AddSingleton<ITimetable>(_timetable);
                services.RemoveAll<INetworkCatalog>();
                services.AddSingleton<INetworkCatalog>(_network);
                services.RemoveAll<IStatusChangeReader>();
                services.AddSingleton<IStatusChangeReader>(_changes);
            });
        _client = _factory.CreateApiClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Theory]
    [InlineData("guard.ishara", Roles.Guard)]
    [InlineData("auditor.wasantha", Roles.Auditor)]
    public async Task Only_the_network_controller_may_read_the_live_view(string subject, string role)
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
        using var response = await _client.GetAsync(JourneysRoute);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_journey_is_not_found()
    {
        using var response = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, DetailRoute(Guid.CreateVersion7()))
                .WithStaffToken(TestTokens.Create("controller.mahesh", Roles.NetworkController)));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_malformed_journey_id_is_a_bad_request()
    {
        using var response = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"{JourneysRoute}/not-a-journey-id")
                .WithStaffToken(TestTokens.Create("controller.mahesh", Roles.NetworkController)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Today_lists_the_journey_with_names_and_its_detail_shows_stops_platforms_and_changes()
    {
        var controller = TestTokens.Create("controller.mahesh", Roles.NetworkController);
        await StartAsync();

        var today = RailwayToday();
        var journeyId = Guid.CreateVersion7();
        var serviceId = Guid.CreateVersion7();
        var lineId = Guid.CreateVersion7();
        var departAt = new DateTimeOffset(today.ToDateTime(new TimeOnly(2, 0)), TimeSpan.Zero);
        var stops = new[]
        {
            new JourneyStopDto(1, StationAlpha, true, null, departAt, PlatformOne),
            new JourneyStopDto(2, StationBeta, true, departAt.AddMinutes(10), departAt.AddMinutes(11), PlatformTwo),
            new JourneyStopDto(3, StationGamma, true, departAt.AddMinutes(20), null, null),
        };

        _timetable.Journeys.Add(new JourneyDto(
            journeyId,
            serviceId,
            "Kandy Express",
            lineId,
            "L1",
            "Main Line",
            ServiceDirection.Outbound,
            today,
            Guid.Parse(GuardStaffId),
            departAt,
            stops));
        _network.Stations =
        [
            new StationDto(StationAlpha, "ALP", "Alpha", false, [new PlatformDto(PlatformOne, StationAlpha, "1")], []),
            new StationDto(StationBeta, "BET", "Beta", false, [new PlatformDto(PlatformTwo, StationBeta, "2")], []),
            new StationDto(StationGamma, "GAM", "Gamma", false, [], []),
        ];
        var changeId = Guid.CreateVersion7();
        _changes.Add(journeyId, new StatusChangeView(
            changeId,
            "delay",
            new Dictionary<string, string?> { ["delayMinutes"] = "5" },
            Guid.Parse(ControllerStaffId),
            departAt.AddMinutes(-1),
            "automatically_published",
            Corrected: false));

        await PublishJourneyAsync(journeyId, serviceId, lineId, today, stops);

        // A journey on another day is not in today's list, however it was generated.
        var otherDayJourneyId = Guid.CreateVersion7();
        await PublishJourneyAsync(otherDayJourneyId, Guid.CreateVersion7(), Guid.CreateVersion7(), today.AddDays(-1), stops);

        using var listResponse = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, JourneysRoute).WithStaffToken(controller));

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var listed = await listResponse.Content.ReadFromJsonAsync<List<JourneyListItem>>();
        Assert.NotNull(listed);
        Assert.DoesNotContain(listed, item => item.Id == otherDayJourneyId);

        var item = Assert.Single(listed, candidate => candidate.Id == journeyId);
        Assert.Equal("Kandy Express", item.ServiceName);
        Assert.Equal("L1", item.LineCode);
        Assert.Equal("Main Line", item.LineName);
        Assert.Equal("outbound", item.Direction);
        Assert.Equal("scheduled", item.Status);
        Assert.Null(item.LastDepartedStation);
        Assert.Equal(GuardName, item.GuardName);
        Assert.False(item.IsStale);

        using var detailResponse = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, DetailRoute(journeyId)).WithStaffToken(controller));

        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await detailResponse.Content.ReadFromJsonAsync<JourneyDetail>();
        Assert.NotNull(detail);
        Assert.Equal(journeyId, detail.Journey.Id);
        Assert.Equal("Kandy Express", detail.Journey.ServiceName);
        Assert.Equal(GuardName, detail.Journey.GuardName);

        Assert.Equal(new[] { 1, 2, 3 }, detail.Stops.Select(stop => stop.Sequence));
        Assert.Equal(new NamedStation(StationAlpha, "Alpha"), detail.Stops[0].Station);
        Assert.Equal(new PlatformItem(PlatformOne, "1"), detail.Stops[0].Platform);
        Assert.Equal(departAt.AddMinutes(10), detail.Stops[1].ScheduledArrival);
        Assert.Null(detail.Stops[2].Platform);

        var change = Assert.Single(detail.Changes);
        Assert.Equal(changeId, change.Id);
        Assert.Equal("delay", change.Type);
        Assert.Equal("5", change.Values["delayMinutes"]);
        Assert.Equal("Mahesh Perera", change.PublisherName);
        Assert.Equal("automatically_published", change.Provenance);
        Assert.False(change.Corrected);
    }

    [Fact]
    public async Task A_journey_the_timetable_does_not_hold_is_still_shown_from_its_live_state()
    {
        var controller = TestTokens.Create("controller.mahesh", Roles.NetworkController);
        await StartAsync();

        var today = RailwayToday();
        var journeyId = Guid.CreateVersion7();
        var departAt = new DateTimeOffset(today.ToDateTime(new TimeOnly(2, 0)), TimeSpan.Zero);
        var stops = new[] { new JourneyStopDto(1, StationAlpha, true, null, departAt, null) };
        await PublishJourneyAsync(journeyId, Guid.CreateVersion7(), Guid.CreateVersion7(), today, stops);

        using var response = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, DetailRoute(journeyId)).WithStaffToken(controller));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await response.Content.ReadFromJsonAsync<JourneyDetail>();
        Assert.NotNull(detail);
        Assert.Null(detail.Journey.ServiceName);
        Assert.Null(detail.Journey.LineCode);
        Assert.Equal("scheduled", detail.Journey.Status);
        Assert.Single(detail.Stops);
        Assert.Empty(detail.Changes);
    }

    private static string DetailRoute(Guid journeyId) => $"{JourneysRoute}/{journeyId}";

    private static DateOnly RailwayToday() => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTime(
            DateTimeOffset.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Colombo")).DateTime);

    /// <summary>Starts the Host, so migrations and seeding have run before a test publishes anything.</summary>
    private async Task StartAsync()
    {
        var ready = await _client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
    }

    private async Task PublishJourneyAsync(
        Guid journeyId,
        Guid serviceId,
        Guid lineId,
        DateOnly serviceDate,
        IReadOnlyList<JourneyStopDto> stops)
    {
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
                Guid.Parse(GuardStaffId),
                stops),
            journeyId);

        await database.SaveChangesAsync();

        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (await LiveStateCountAsync(journeyId) == 0 && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        Assert.Equal(1, await LiveStateCountAsync(journeyId));
    }

    private async Task<int> LiveStateCountAsync(Guid journeyId)
    {
        var count = Assert.Single(await _postgres.QueryAsync(
            $"SELECT count(*) FROM operations.journey_live_states WHERE journey_id = '{journeyId}'",
            reader => reader.GetInt64(0)));

        return (int)count;
    }

    private sealed record NamedStation(Guid Id, string Name);

    private sealed record PlatformItem(Guid Id, string Number);

    private sealed record JourneyListItem(
        Guid Id,
        string? ServiceName,
        string? LineCode,
        string? LineName,
        string? Direction,
        string Status,
        int? DelayMinutes,
        NamedStation? LastDepartedStation,
        NamedStation? NextStation,
        string? GuardName,
        DateTimeOffset LastUpdatedAt,
        bool IsStale);

    private sealed record StopItem(
        int Sequence,
        NamedStation? Station,
        bool IsCalling,
        DateTimeOffset? ScheduledArrival,
        DateTimeOffset? ScheduledDeparture,
        DateTimeOffset? ExpectedArrival,
        DateTimeOffset? ExpectedDeparture,
        DateTimeOffset? ActualDeparture,
        PlatformItem? Platform);

    private sealed record ChangeItem(
        Guid Id,
        string Type,
        IReadOnlyDictionary<string, string?> Values,
        string? PublisherName,
        DateTimeOffset PublishedAt,
        string Provenance,
        bool Corrected);

    private sealed record JourneyDetail(
        JourneyListItem Journey,
        IReadOnlyList<StopItem> Stops,
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

    /// <summary>The published changes per journey, substituted because the status_changes table arrives later.</summary>
    private sealed class FakeStatusChangeReader : IStatusChangeReader
    {
        private readonly Dictionary<Guid, List<StatusChangeView>> _byJourney = [];

        public void Add(Guid journeyId, StatusChangeView change)
        {
            if (!_byJourney.TryGetValue(journeyId, out var changes))
            {
                changes = [];
                _byJourney[journeyId] = changes;
            }

            changes.Add(change);
        }

        public Task<IReadOnlyList<StatusChangeView>> GetForJourneyAsync(
            Guid journeyId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StatusChangeView>>(
                _byJourney.TryGetValue(journeyId, out var changes) ? changes : []);
    }
}
