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

namespace SmartRail.Host.IntegrationTests.Public;

/// <summary>
/// The station display feed against the real seeded network and timetable (task 3.1,
/// station-display-feed spec): the seeded interchange platform with its next departures in
/// expected-departure order, the count default and its limits, an unknown station and an unknown
/// platform number, a platform change that moves a train to the new platform's feed and announces it
/// on the old one, the same journey read through the display and the passenger departures, and the
/// freshness and uncacheable response every public response carries.
/// </summary>
/// <remarks>
/// The Host runs with a fixed clock at noon in the railway's time zone on the day's date, so the
/// seeded departures around it are the same whenever the suite runs. The journeys the seed cannot
/// express are created through the real generation announcement with the seeded service they belong
/// to, and their state is driven through the real controller endpoints, as the other public tests do.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class PublicDisplayTests
{
    private const string ControllerSubject = "controller.mahesh";

    /// <summary>The seeded guard account (seed/identity/staff.json), which owns every seeded service.</summary>
    private static readonly Guid GuardStaffId = Guid.Parse("01a15874-e69d-73b2-b24d-d85d22fdead2");

    /// <summary>The interchange station on the seeded network (seed/network/network.json).</summary>
    private static readonly Guid ColomboFort = Guid.Parse("04624d0f-9410-72e3-967f-501ed71f2441");

    private static readonly Guid Kollupitiya = Guid.Parse("dc1ed19b-f522-70a9-ae61-15e47102a42f");
    private static readonly Guid MountLavinia = Guid.Parse("c078a9aa-2f80-7422-a65b-bb7313795bd5");

    /// <summary>Colombo Fort's platforms 2 and 3, the pair a platform change moves a stop between.</summary>
    private static readonly Guid ColomboFortPlatformTwo = Guid.Parse("61e67fba-522e-73bf-beab-5cb90e861c86");
    private static readonly Guid ColomboFortPlatformThree = Guid.Parse("573394b2-45be-7539-9a43-fbc6448c7ad7");

    private static readonly Guid KollupitiyaPlatform = Guid.Parse("e86062e1-aa04-7673-b2dc-51892f077a58");
    private static readonly Guid MountLaviniaPlatform = Guid.Parse("579f8b1e-e3fe-789b-9ba3-20aae4342df3");

    /// <summary>The seeded Coastal Line (seed/network/network.json), which serves the stations above.</summary>
    private static readonly Guid CoastalLine = Guid.Parse("f8493f1c-67b5-76e1-acb6-215d4d9f60ae");

    /// <summary>
    /// The seeded "Coastal 1210 Outbound" service (seed/network/timetable.json). Its first stop is
    /// Colombo Fort platform 2, so the timetable's own platform is the one a change moves away from.
    /// </summary>
    private static readonly Guid Coastal1210 = Guid.Parse("3448e508-b0cc-71d2-a7ce-1be9b30683c0");

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private readonly PostgresFixture _postgres;

    public PublicDisplayTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task A_platform_lists_the_seeded_departures_assigned_to_it_in_expected_departure_order()
    {
        var now = SafeNow(RailwayToday());
        using var factory = NewFactory(now);
        using var client = factory.CreateApiClient();

        using var document = await WaitForSeededFeedAsync(client);

        var root = document.RootElement;
        Assert.Equal(now, root.GetProperty("generatedAt").GetDateTimeOffset());
        Assert.Equal(15, root.GetProperty("refreshAfterSeconds").GetInt32());

        var data = root.GetProperty("data");
        var departures = data.GetProperty("departures").EnumerateArray().ToArray();

        // At noon the next two hours hold the one Coastal and the one Main departure from the
        // interchange, and both are assigned to platform 2.
        Assert.Equal(2, departures.Length);
        Assert.All(departures, departure =>
        {
            Assert.Equal("2", departure.GetProperty("origin").GetProperty("platform").GetProperty("number").GetString());
            Assert.Equal(ColomboFort, departure.GetProperty("origin").GetProperty("station").GetProperty("id").GetGuid());
            Assert.Equal("scheduled", departure.GetProperty("journey").GetProperty("status").GetString());
            Assert.False(string.IsNullOrWhiteSpace(
                departure.GetProperty("destination").GetProperty("name").GetString()));
            Assert.True(
                departure.GetProperty("journey").TryGetProperty("lastUpdatedAt", out _),
                "Every item must carry the last-updated time of the live state it was built from.");
        });

        var expectedDepartures = departures
            .Select(departure => departure.GetProperty("origin").GetProperty("expectedDeparture").GetDateTimeOffset())
            .ToArray();

        Assert.Equal(expectedDepartures.Order().ToArray(), expectedDepartures);

        // Nothing is announced as moved on the untouched seeded platform.
        Assert.Equal(0, data.GetProperty("movedNotices").GetArrayLength());

        // The same station's other platforms are served, and one with no departure is an empty board.
        using var unused = await GetOkAsync(client, FeedRoute(ColomboFort, "1"));
        Assert.Equal(0, unused.RootElement.GetProperty("data").GetProperty("departures").GetArrayLength());

        // Platform 3 only receives the inbound trains, and an arrival is not a departure.
        using var arrivalOnly = await GetOkAsync(client, FeedRoute(ColomboFort, "3"));
        Assert.Equal(0, arrivalOnly.RootElement.GetProperty("data").GetProperty("departures").GetArrayLength());

        // Every public response is uncacheable and sets no cookie (design D7).
        using var raw = await client.GetAsync(FeedRoute(ColomboFort, "2"));
        Assert.Equal(HttpStatusCode.OK, raw.StatusCode);
        Assert.Equal("no-store", raw.Headers.CacheControl?.ToString());
        Assert.False(raw.Headers.TryGetValues("Set-Cookie", out _));
    }

    [Fact]
    public async Task The_count_is_eight_by_default_and_twenty_at_most()
    {
        var now = SafeNow(RailwayToday());
        using var factory = NewFactory(now);
        using var client = factory.CreateApiClient();
        await WaitForSeededFeedAsync(client);

        var tooLarge = await GetAsync(client, $"{FeedRoute(ColomboFort, "2")}?count=21");
        Assert.Equal(HttpStatusCode.BadRequest, tooLarge.Status);
        Assert.Equal("application/problem+json", tooLarge.MediaType);
        Assert.Contains("count", tooLarge.Body, StringComparison.Ordinal);

        var notPositive = await GetAsync(client, $"{FeedRoute(ColomboFort, "2")}?count=0");
        Assert.Equal(HttpStatusCode.BadRequest, notPositive.Status);
        Assert.Contains("count", notPositive.Body, StringComparison.Ordinal);

        using var byDefault = await GetOkAsync(client, FeedRoute(ColomboFort, "2"));
        using var byMaximum = await GetOkAsync(client, $"{FeedRoute(ColomboFort, "2")}?count=20");
        using var byOne = await GetOkAsync(client, $"{FeedRoute(ColomboFort, "2")}?count=1");

        Assert.Equal(2, byDefault.RootElement.GetProperty("data").GetProperty("departures").GetArrayLength());
        Assert.Equal(
            JourneyIds(byDefault.RootElement.GetProperty("data").GetProperty("departures")),
            JourneyIds(byMaximum.RootElement.GetProperty("data").GetProperty("departures")));
        Assert.Single(byOne.RootElement.GetProperty("data").GetProperty("departures").EnumerateArray());
    }

    [Fact]
    public async Task An_unknown_station_or_an_unknown_platform_number_is_not_found()
    {
        using var factory = NewFactory(SafeNow(RailwayToday()));
        using var client = factory.CreateApiClient();
        await StartAsync(factory);

        var unknownStation = await GetAsync(client, FeedRoute(Guid.CreateVersion7(), "1"));
        Assert.Equal(HttpStatusCode.NotFound, unknownStation.Status);
        Assert.Equal("application/problem+json", unknownStation.MediaType);

        // Colombo Fort has platforms 1, 2 and 3, so a number outside that set is not a platform.
        var unknownPlatform = await GetAsync(client, FeedRoute(ColomboFort, "9"));
        Assert.Equal(HttpStatusCode.NotFound, unknownPlatform.Status);
        Assert.Equal("application/problem+json", unknownPlatform.MediaType);
    }

    [Fact]
    public async Task A_replatformed_train_leaves_its_old_platform_and_is_announced_there()
    {
        var today = RailwayToday();
        var now = SafeNow(today);
        using var factory = NewFactory(now);
        var journeyId = await CreateJourneyAsync(
            factory,
            today,
            Coastal1210,
            [
                Stop(1, ColomboFort, ColomboFortPlatformTwo, null, now.AddMinutes(60)),
                Stop(2, Kollupitiya, KollupitiyaPlatform, now.AddMinutes(70), now.AddMinutes(71)),
                Stop(3, MountLavinia, MountLaviniaPlatform, now.AddMinutes(90), null),
            ]);

        var auditBaseline = await MaxAuditPositionAsync();

        try
        {
            using var client = factory.CreateApiClient();

            using var changed = await PostPlatformChangeAsync(
                client,
                journeyId,
                stopSequence: 1,
                platformId: ColomboFortPlatformThree);
            Assert.Equal(HttpStatusCode.Created, changed.StatusCode);

            using var change = JsonDocument.Parse(await changed.Content.ReadAsStringAsync());
            var publishedAt = change.RootElement.GetProperty("publishedAt").GetDateTimeOffset();

            // The new platform's feed lists it, flagged with the time the change became known.
            using var newPlatform = await GetOkAsync(client, FeedRoute(ColomboFort, "3"));
            var moved = Assert.Single(
                newPlatform.RootElement.GetProperty("data").GetProperty("departures").EnumerateArray(),
                departure => JourneyIdOf(departure) == journeyId);

            Assert.Equal("3", moved.GetProperty("origin").GetProperty("platform").GetProperty("number").GetString());
            Assert.Equal(
                publishedAt,
                moved.GetProperty("origin").GetProperty("platformChangedAt").GetDateTimeOffset());
            Assert.DoesNotContain(
                journeyId,
                NoticeIds(newPlatform.RootElement.GetProperty("data").GetProperty("movedNotices")));

            // The old platform no longer lists it as a departure, and announces where it went.
            using var oldPlatform = await GetOkAsync(client, FeedRoute(ColomboFort, "2"));
            var oldData = oldPlatform.RootElement.GetProperty("data");

            Assert.DoesNotContain(
                journeyId,
                JourneyIds(oldData.GetProperty("departures")));

            var notice = Assert.Single(
                oldData.GetProperty("movedNotices").EnumerateArray(),
                candidate => candidate.GetProperty("journeyId").GetGuid() == journeyId);

            Assert.Equal(now.AddMinutes(60), notice.GetProperty("scheduledDeparture").GetDateTimeOffset());
            Assert.Equal("3", notice.GetProperty("newPlatform").GetProperty("number").GetString());
            Assert.Equal(MountLavinia, notice.GetProperty("destination").GetProperty("id").GetGuid());
            Assert.Contains("has moved to platform 3", notice.GetProperty("message").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            await CleanUpAsync([journeyId], auditBaseline);
        }
    }

    [Fact]
    public async Task The_same_journey_is_identical_through_the_display_and_the_station_departures()
    {
        var today = RailwayToday();
        var now = SafeNow(today);
        using var factory = NewFactory(now);
        var journeyId = await CreateJourneyAsync(
            factory,
            today,
            Coastal1210,
            [
                Stop(1, ColomboFort, ColomboFortPlatformTwo, null, now.AddMinutes(60)),
                Stop(2, Kollupitiya, KollupitiyaPlatform, now.AddMinutes(70), now.AddMinutes(71)),
                Stop(3, MountLavinia, MountLaviniaPlatform, now.AddMinutes(90), null),
            ]);

        var auditBaseline = await MaxAuditPositionAsync();

        try
        {
            using var client = factory.CreateApiClient();

            using var delayed = await PostDelayAsync(client, journeyId, fromStopSequence: 1, minutes: 15);
            Assert.Equal(HttpStatusCode.Created, delayed.StatusCode);

            using var display = await GetOkAsync(client, FeedRoute(ColomboFort, "2"));
            using var station = await GetOkAsync(
                client,
                $"{ApiRoutes.BasePath}/public/stations/{ColomboFort}/departures");

            var displayItem = Assert.Single(
                display.RootElement.GetProperty("data").GetProperty("departures").EnumerateArray(),
                candidate => JourneyIdOf(candidate) == journeyId);
            var stationItem = Assert.Single(
                station.RootElement.GetProperty("data").EnumerateArray(),
                candidate => JourneyIdOf(candidate) == journeyId);

            // One view builder produces both, so the times, the status, the platform change and the
            // last-updated time are not merely equal but the same serialised item.
            Assert.Equal(stationItem.GetRawText(), displayItem.GetRawText());
            Assert.Equal(
                now.AddMinutes(75),
                displayItem.GetProperty("origin").GetProperty("expectedDeparture").GetDateTimeOffset());
            Assert.Equal(15, displayItem.GetProperty("minutesLate").GetInt32());
        }
        finally
        {
            await CleanUpAsync([journeyId], auditBaseline);
        }
    }

    private SmartRailApiFactory NewFactory(DateTimeOffset now) =>
        new(
            _postgres.ConnectionString,
            configureServices: services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(now));
            });

    private static string FeedRoute(Guid stationId, string platformNumber) =>
        $"{ApiRoutes.BasePath}/public/display/{stationId}/platforms/{platformNumber}";

    private static async Task StartAsync(SmartRailApiFactory factory)
    {
        using var client = factory.CreateApiClient();
        using var response = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// Reads the interchange's platform 2 board once the start-up generation has produced the seeded
    /// journeys. The hosted service runs after <c>app.Run()</c>, so the first request may meet an
    /// empty board.
    /// </summary>
    private static async Task<JsonDocument> WaitForSeededFeedAsync(HttpClient client)
    {
        var route = FeedRoute(ColomboFort, "2");
        var deadline = DateTimeOffset.UtcNow.Add(Timeout);

        while (true)
        {
            using var response = await client.GetAsync(route);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            if (document.RootElement.GetProperty("data").GetProperty("departures").GetArrayLength() > 0)
            {
                return document;
            }

            document.Dispose();

            Assert.True(
                DateTimeOffset.UtcNow < deadline,
                $"The seeded journeys did not appear within {Timeout}.");

            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
    }

    private static async Task<JsonDocument> GetOkAsync(HttpClient client, string route)
    {
        using var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static async Task<(HttpStatusCode Status, string? MediaType, string Body)> GetAsync(
        HttpClient client,
        string route)
    {
        using var response = await client.GetAsync(route);

        return (
            response.StatusCode,
            response.Content.Headers.ContentType?.MediaType,
            await response.Content.ReadAsStringAsync());
    }

    /// <summary>Creates a journey's live state through the real generation announcement.</summary>
    private async Task<Guid> CreateJourneyAsync(
        SmartRailApiFactory factory,
        DateOnly serviceDate,
        Guid serviceId,
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
                serviceId,
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

    private static async Task<HttpResponseMessage> PostPlatformChangeAsync(
        HttpClient client,
        Guid journeyId,
        int stopSequence,
        Guid platformId)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{ApiRoutes.BasePath}/operations/journeys/{journeyId}/platform-changes")
        {
            Content = JsonContent.Create(new { stopSequence, platformId }),
        };
        request.WithStaffToken(TestTokens.Create(ControllerSubject, Roles.NetworkController));

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostDelayAsync(
        HttpClient client,
        Guid journeyId,
        int fromStopSequence,
        int minutes)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{ApiRoutes.BasePath}/operations/journeys/{journeyId}/delays")
        {
            Content = JsonContent.Create(new { fromStopSequence, minutes }),
        };
        request.WithStaffToken(TestTokens.Create(ControllerSubject, Roles.NetworkController));

        return await client.SendAsync(request);
    }

    private static JourneyStopDto Stop(
        int sequence,
        Guid stationId,
        Guid? platformId,
        DateTimeOffset? arrival,
        DateTimeOffset? departure) =>
        new(sequence, stationId, IsCalling: true, arrival, departure, platformId);

    private static Guid JourneyIdOf(JsonElement item) =>
        item.GetProperty("journey").GetProperty("journeyId").GetGuid();

    private static Guid[] JourneyIds(JsonElement items) =>
        [.. items.EnumerateArray().Select(JourneyIdOf)];

    private static Guid[] NoticeIds(JsonElement notices) =>
        [.. notices.EnumerateArray().Select(notice => notice.GetProperty("journeyId").GetGuid())];

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

    private async Task<long> MaxAuditPositionAsync() =>
        Assert.Single(await _postgres.QueryAsync(
            "SELECT coalesce(max(chain_position), 0) FROM audit.entries",
            reader => reader.GetInt64(0)));

    private async Task<int> CountAsync(string sql)
    {
        var count = Assert.Single(await _postgres.QueryAsync(sql, reader => reader.GetInt64(0)));
        return (int)count;
    }

    /// <summary>Removes every row the test created for its journey, then the audit entries it appended.</summary>
    private async Task CleanUpAsync(IReadOnlyList<Guid> journeyIds, long auditBaseline)
    {
        foreach (var journeyId in journeyIds)
        {
            await _postgres.ExecuteAsync(
                $"DELETE FROM operations.departure_logs WHERE journey_id = '{journeyId}'");
            await _postgres.ExecuteAsync(
                $"""
                DELETE FROM operations.status_changes
                WHERE journey_id = '{journeyId}' AND corrects_change_id IS NOT NULL
                """);
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

        await ResetAuditEntriesAsync(auditBaseline);
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

    /// <summary>A clock frozen at one instant, so "now" and the seeded timetable line up exactly.</summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
