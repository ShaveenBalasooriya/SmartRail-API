using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
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
/// The public journey detail and the conventions every public response shares (tasks 2.3 and 2.4):
/// every stop in order with its calling flag, its times and its platform, the skipped station of a
/// limited-stop journey, a platform change with the time it became known and its provenance, a
/// corrected cancellation, cancellation guidance, the unknown journey, anonymous access, the
/// uncacheable responses with no cookie, the rate-limited 429 that is uncacheable too, the request
/// log holding neither the caller's address nor a query value, and the same journey read through the
/// departures list and the detail being identical (design D1, D7).
/// </summary>
/// <remarks>
/// The Host runs with a fixed clock at noon in the railway's time zone on the day's date, so the
/// seeded network and timetable are the same whenever the suite runs. Journeys the seed cannot
/// express are created through the real generation announcement, and their live state is driven
/// through the real Guard and controller endpoints, as the departures tests do.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class PublicJourneyDetailTests
{
    private const string GuardSubject = "guard.ishara";

    private const string ControllerSubject = "controller.mahesh";

    /// <summary>The seeded guard account (seed/identity/staff.json), which owns every seeded service.</summary>
    private static readonly Guid GuardStaffId = Guid.Parse("01a15874-e69d-73b2-b24d-d85d22fdead2");

    /// <summary>The interchange station on the seeded network (seed/network/network.json).</summary>
    private static readonly Guid ColomboFort = Guid.Parse("04624d0f-9410-72e3-967f-501ed71f2441");

    private static readonly Guid Kollupitiya = Guid.Parse("dc1ed19b-f522-70a9-ae61-15e47102a42f");
    private static readonly Guid Bambalapitiya = Guid.Parse("6f818201-ebf9-7374-bbad-74adbab9df38");
    private static readonly Guid MountLavinia = Guid.Parse("c078a9aa-2f80-7422-a65b-bb7313795bd5");

    /// <summary>Colombo Fort's platforms 2 and 3, the pair a platform change moves a stop between.</summary>
    private static readonly Guid ColomboFortPlatformTwo = Guid.Parse("61e67fba-522e-73bf-beab-5cb90e861c86");
    private static readonly Guid ColomboFortPlatformThree = Guid.Parse("573394b2-45be-7539-9a43-fbc6448c7ad7");

    private static readonly Guid KollupitiyaPlatform = Guid.Parse("e86062e1-aa04-7673-b2dc-51892f077a58");
    private static readonly Guid BambalapitiyaPlatform = Guid.Parse("8f4b8be6-fea4-7c57-bd1d-64638e6273af");
    private static readonly Guid MountLaviniaPlatform = Guid.Parse("579f8b1e-e3fe-789b-9ba3-20aae4342df3");

    /// <summary>The seeded Coastal Line (seed/network/network.json), which serves the stations above.</summary>
    private static readonly Guid CoastalLine = Guid.Parse("f8493f1c-67b5-76e1-acb6-215d4d9f60ae");

    /// <summary>A caller address the request log must never carry (diagnostic-logging spec, EP-14).</summary>
    private const string ClientIp = "203.0.113.77";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private readonly PostgresFixture _postgres;

    public PublicJourneyDetailTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task The_detail_lists_every_stop_in_order_with_its_calling_flag_platform_and_freshness()
    {
        var today = RailwayToday();
        var now = SafeNow(today);
        using var factory = NewFactory(now);
        var journeyId = await CreateJourneyAsync(
            factory,
            today,
            [
                Stop(1, ColomboFort, ColomboFortPlatformTwo, null, now.AddMinutes(60)),
                Stop(2, Kollupitiya, KollupitiyaPlatform, now.AddMinutes(70), now.AddMinutes(71)),
                Stop(3, Bambalapitiya, BambalapitiyaPlatform, now.AddMinutes(80), now.AddMinutes(81)),
                Stop(4, MountLavinia, MountLaviniaPlatform, now.AddMinutes(90), null),
            ]);

        var auditBaseline = await MaxAuditPositionAsync();

        try
        {
            using var client = factory.CreateApiClient();
            using var document = await GetOkAsync(client, JourneyRoute(journeyId));
            var root = document.RootElement;
            var data = root.GetProperty("data");

            // Freshness: the envelope says when the response was generated, the journey says when
            // its live state last changed (passenger-departures spec, freshness on every item).
            Assert.Equal(now, root.GetProperty("generatedAt").GetDateTimeOffset());
            Assert.Equal(15, root.GetProperty("refreshAfterSeconds").GetInt32());
            Assert.Equal(now, data.GetProperty("lastUpdatedAt").GetDateTimeOffset());

            Assert.Equal(journeyId, data.GetProperty("journeyId").GetGuid());
            Assert.Equal(today, DateOnly.FromDateTime(data.GetProperty("serviceDate").GetDateTime()));
            Assert.Equal("scheduled", data.GetProperty("status").GetString());
            Assert.Equal("Coastal Line", data.GetProperty("line").GetProperty("name").GetString());
            Assert.Equal("#1d4ed8", data.GetProperty("line").GetProperty("color").GetString());

            var stops = data.GetProperty("stops").EnumerateArray().ToArray();

            Assert.Equal([1, 2, 3, 4], stops.Select(stop => stop.GetProperty("sequence").GetInt32()).ToArray());
            Assert.All(stops, stop => Assert.True(stop.GetProperty("isCalling").GetBoolean()));
            Assert.Equal(
                [ColomboFort, Kollupitiya, Bambalapitiya, MountLavinia],
                stops.Select(stop => stop.GetProperty("station").GetProperty("id").GetGuid()).ToArray());

            Assert.Equal("Colombo Fort", stops[0].GetProperty("station").GetProperty("name").GetString());
            Assert.Equal(now.AddMinutes(60), stops[0].GetProperty("scheduledDeparture").GetDateTimeOffset());
            Assert.Equal(now.AddMinutes(60), stops[0].GetProperty("expectedDeparture").GetDateTimeOffset());
            Assert.Equal("2", stops[0].GetProperty("platform").GetProperty("number").GetString());

            Assert.Equal(now.AddMinutes(70), stops[1].GetProperty("scheduledArrival").GetDateTimeOffset());
            Assert.Equal(now.AddMinutes(71), stops[1].GetProperty("scheduledDeparture").GetDateTimeOffset());
            Assert.True(IsNullOrAbsent(stops[1], "actualDeparture"));

            // The terminus has an arrival and no departure, and nothing has been recorded at it.
            Assert.Equal(now.AddMinutes(90), stops[3].GetProperty("scheduledArrival").GetDateTimeOffset());
            Assert.True(IsNullOrAbsent(stops[3], "scheduledDeparture"));
        }
        finally
        {
            await CleanUpAsync([journeyId], auditBaseline);
        }
    }

    [Fact]
    public async Task A_limited_stop_journey_marks_the_skipped_station_as_not_calling_with_no_times_or_platform()
    {
        var today = RailwayToday();
        var now = SafeNow(today);
        using var factory = NewFactory(now);
        var journeyId = await CreateJourneyAsync(
            factory,
            today,
            [
                Stop(1, ColomboFort, ColomboFortPlatformTwo, null, now.AddMinutes(60)),
                Stop(2, Kollupitiya, KollupitiyaPlatform, now.AddMinutes(70), now.AddMinutes(71)),
                Stop(3, Bambalapitiya, null, null, null, isCalling: false),
                Stop(4, MountLavinia, MountLaviniaPlatform, now.AddMinutes(90), null),
            ]);

        var auditBaseline = await MaxAuditPositionAsync();

        try
        {
            using var client = factory.CreateApiClient();
            using var document = await GetOkAsync(client, JourneyRoute(journeyId));
            var stops = document.RootElement.GetProperty("data").GetProperty("stops").EnumerateArray().ToArray();

            var skipped = Assert.Single(stops, stop => stop.GetProperty("sequence").GetInt32() == 3);

            Assert.Equal(Bambalapitiya, skipped.GetProperty("station").GetProperty("id").GetGuid());
            Assert.False(skipped.GetProperty("isCalling").GetBoolean());
            Assert.True(IsNullOrAbsent(skipped, "scheduledArrival"));
            Assert.True(IsNullOrAbsent(skipped, "scheduledDeparture"));
            Assert.True(IsNullOrAbsent(skipped, "expectedArrival"));
            Assert.True(IsNullOrAbsent(skipped, "expectedDeparture"));
            Assert.True(IsNullOrAbsent(skipped, "actualDeparture"));
            Assert.True(IsNullOrAbsent(skipped, "platform"));
            Assert.True(IsNullOrAbsent(skipped, "platformChangedAt"));
            Assert.True(IsNullOrAbsent(skipped, "provenance"));
        }
        finally
        {
            await CleanUpAsync([journeyId], auditBaseline);
        }
    }

    [Fact]
    public async Task A_platform_change_shows_the_new_platform_its_time_and_its_provenance()
    {
        var today = RailwayToday();
        var now = SafeNow(today);
        using var factory = NewFactory(now);
        var journeyId = await CreateJourneyAsync(
            factory,
            today,
            [
                Stop(1, ColomboFort, ColomboFortPlatformTwo, null, now.AddMinutes(60)),
                Stop(2, MountLavinia, MountLaviniaPlatform, now.AddMinutes(90), null),
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

            using var document = await GetOkAsync(client, JourneyRoute(journeyId));
            var data = document.RootElement.GetProperty("data");
            var stop = data.GetProperty("stops")[0];

            Assert.Equal("3", stop.GetProperty("platform").GetProperty("number").GetString());
            Assert.Equal(ColomboFortPlatformThree, stop.GetProperty("platform").GetProperty("id").GetGuid());
            Assert.True(
                (stop.GetProperty("platformChangedAt").GetDateTimeOffset() - publishedAt).Duration()
                    < TimeSpan.FromMilliseconds(1),
                "the stop must show the time the platform change became known.");
            Assert.Equal("automatically_published", stop.GetProperty("provenance").GetString());
            Assert.Equal("automatically_published", data.GetProperty("provenance").GetString());
        }
        finally
        {
            await CleanUpAsync([journeyId], auditBaseline);
        }
    }

    [Fact]
    public async Task A_cancelled_journey_carries_cancellation_guidance()
    {
        var today = RailwayToday();
        var now = SafeNow(today);
        using var factory = NewFactory(now);
        var journeyId = await CreateJourneyAsync(factory, today, TodayStops(now));

        var auditBaseline = await MaxAuditPositionAsync();

        try
        {
            using var client = factory.CreateApiClient();

            using var cancelled = await PostCancellationAsync(client, journeyId);
            Assert.Equal(HttpStatusCode.Created, cancelled.StatusCode);

            using var document = await GetOkAsync(client, JourneyRoute(journeyId));
            var data = document.RootElement.GetProperty("data");

            Assert.Equal("cancelled", data.GetProperty("status").GetString());
            Assert.Equal("automatically_published", data.GetProperty("provenance").GetString());

            var guidance = data.GetProperty("guidance");
            Assert.Equal("cancellation", guidance.GetProperty("type").GetString());
            Assert.Contains("is cancelled", guidance.GetProperty("message").GetString(), StringComparison.Ordinal);
            Assert.Contains("Mount Lavinia", guidance.GetProperty("message").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            await CleanUpAsync([journeyId], auditBaseline);
        }
    }

    [Fact]
    public async Task A_corrected_cancellation_shows_the_restored_status_and_the_correction_time()
    {
        var today = RailwayToday();
        var now = SafeNow(today);
        using var factory = NewFactory(now);
        var journeyId = await CreateJourneyAsync(factory, today, TodayStops(now));

        var auditBaseline = await MaxAuditPositionAsync();

        try
        {
            using var client = factory.CreateApiClient();

            using var cancelled = await PostCancellationAsync(client, journeyId);
            Assert.Equal(HttpStatusCode.Created, cancelled.StatusCode);

            using var cancellation = JsonDocument.Parse(await cancelled.Content.ReadAsStringAsync());
            var changeId = cancellation.RootElement.GetProperty("id").GetGuid();

            using var corrected = await PostCorrectionAsync(client, changeId);
            Assert.Equal(HttpStatusCode.Created, corrected.StatusCode);

            using var correction = JsonDocument.Parse(await corrected.Content.ReadAsStringAsync());
            var correctedAt = correction.RootElement.GetProperty("publishedAt").GetDateTimeOffset();

            using var document = await GetOkAsync(client, JourneyRoute(journeyId));
            var data = document.RootElement.GetProperty("data");

            // The false state is gone from the current values, and the journey says it was corrected.
            Assert.Equal("scheduled", data.GetProperty("status").GetString());
            Assert.Equal(correctedAt, data.GetProperty("correctedAt").GetDateTimeOffset());
            Assert.True(IsNullOrAbsent(data, "provenance"));
        }
        finally
        {
            await CleanUpAsync([journeyId], auditBaseline);
        }
    }

    [Fact]
    public async Task An_unknown_journey_is_not_found()
    {
        using var factory = NewFactory(SafeNow(RailwayToday()));
        using var client = factory.CreateApiClient();
        await StartAsync(factory);

        var response = await GetAsync(client, JourneyRoute(Guid.CreateVersion7()));

        Assert.Equal(HttpStatusCode.NotFound, response.Status);
        Assert.Equal("application/problem+json", response.MediaType);
    }

    [Fact]
    public async Task An_anonymous_request_is_served_without_a_token()
    {
        var today = RailwayToday();
        var now = SafeNow(today);
        using var factory = NewFactory(now);
        var journeyId = await CreateJourneyAsync(factory, today, TodayStops(now));

        var auditBaseline = await MaxAuditPositionAsync();

        try
        {
            // A client that never sends an Authorization header: the passenger app has no account.
            using var client = factory.CreateApiClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, JourneyRoute(journeyId));
            using var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            await CleanUpAsync([journeyId], auditBaseline);
        }
    }

    [Fact]
    public async Task Every_public_response_is_uncacheable_and_sets_no_cookie()
    {
        var today = RailwayToday();
        var now = SafeNow(today);
        using var factory = NewFactory(now);
        var journeyId = await CreateJourneyAsync(factory, today, TodayStops(now));

        var auditBaseline = await MaxAuditPositionAsync();

        try
        {
            // Cookies are not handled by the client, so a Set-Cookie header would be visible here
            // rather than being swallowed into a cookie container.
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = false,
            });

            var ok = await GetAsync(client, JourneyRoute(journeyId));
            var invalid = await GetAsync(client, JourneyRoute(Guid.Empty));
            var unknown = await GetAsync(client, JourneyRoute(Guid.CreateVersion7()));

            Assert.Equal(HttpStatusCode.OK, ok.Status);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.Status);
            Assert.Equal("application/problem+json", invalid.MediaType);
            Assert.Contains("id", invalid.Body, StringComparison.Ordinal);
            Assert.Equal(HttpStatusCode.NotFound, unknown.Status);

            foreach (var response in new[] { ok, invalid, unknown })
            {
                Assert.Equal("no-store", response.CacheControl);
                Assert.False(response.HasSetCookie, "a public response must set no cookie.");
            }
        }
        finally
        {
            await CleanUpAsync([journeyId], auditBaseline);
        }
    }

    [Fact]
    public async Task A_request_over_the_public_limit_is_refused_with_a_429_that_is_also_uncacheable()
    {
        using var factory = new SmartRailApiFactory(
            _postgres.ConnectionString,
            settings: new Dictionary<string, string>
            {
                ["RateLimiting:Public:PermitLimit"] = "2",
            });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
        await StartAsync(factory);

        var route = JourneyRoute(Guid.CreateVersion7());

        using var first = await client.GetAsync(route);
        using var second = await client.GetAsync(route);
        using var third = await client.GetAsync(route);

        // The detail of an unknown journey is a 404, which still counts: the limit is on requests.
        Assert.Equal(HttpStatusCode.NotFound, first.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Equal("application/problem+json", third.Content.Headers.ContentType?.MediaType);
        Assert.True(third.Headers.RetryAfter is not null, "a 429 must tell the client when to retry.");
        Assert.Equal("no-store", third.Headers.CacheControl?.ToString());
        Assert.False(third.Headers.TryGetValues("Set-Cookie", out _));

        using var document = JsonDocument.Parse(await third.Content.ReadAsStringAsync());
        Assert.Equal(429, document.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task No_caller_address_or_query_value_reaches_the_request_log()
    {
        using var factory = NewFactory(SafeNow(RailwayToday()));
        using var client = factory.CreateApiClient();
        await StartAsync(factory);

        var unknownJourney = Guid.CreateVersion7();
        var routes = new[]
        {
            $"{ApiRoutes.BasePath}/public/departures?from={ColomboFort}&to={Kollupitiya}",
            $"{ApiRoutes.BasePath}/public/stations/{ColomboFort}/departures",
            JourneyRoute(unknownJourney),
        };

        foreach (var route in routes)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, route);
            request.Headers.Add("X-Forwarded-For", ClientIp);

            using var response = await client.SendAsync(request);
            Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        }

        await WaitForRequestLogsAsync(factory.Logs, routes.Length);

        var log = factory.Logs.AllText;

        Assert.DoesNotContain(ClientIp, log, StringComparison.Ordinal);
        Assert.DoesNotContain(ColomboFort.ToString(), log, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Kollupitiya.ToString(), log, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(unknownJourney.ToString(), log, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_same_journey_through_the_departures_and_the_detail_is_identical()
    {
        var today = RailwayToday();
        var now = SafeNow(today);
        using var factory = NewFactory(now);
        var journeyId = await CreateJourneyAsync(
            factory,
            today,
            [
                Stop(1, ColomboFort, ColomboFortPlatformTwo, null, now.AddMinutes(30)),
                Stop(2, Kollupitiya, KollupitiyaPlatform, now.AddMinutes(40), now.AddMinutes(41)),
                Stop(3, MountLavinia, MountLaviniaPlatform, now.AddMinutes(55), null),
            ]);

        var auditBaseline = await MaxAuditPositionAsync();

        try
        {
            using var client = factory.CreateApiClient();

            using var departures = await GetOkAsync(
                client,
                $"{ApiRoutes.BasePath}/public/stations/{ColomboFort}/departures");
            var item = Assert.Single(
                departures.RootElement.GetProperty("data").EnumerateArray(),
                candidate => candidate.GetProperty("journey").GetProperty("journeyId").GetGuid() == journeyId);

            using var detail = await GetOkAsync(client, JourneyRoute(journeyId));

            // One builder produces both, so status, every stop's times and platforms, the guidance
            // and the last-updated time are not merely equal but the same serialised view.
            Assert.Equal(
                item.GetProperty("journey").GetRawText(),
                detail.RootElement.GetProperty("data").GetRawText());
        }
        finally
        {
            await CleanUpAsync([journeyId], auditBaseline);
        }
    }

    /// <summary>Three calling stops later today, the shape a published change is checked against.</summary>
    private static IReadOnlyList<JourneyStopDto> TodayStops(DateTimeOffset now) =>
    [
        Stop(1, ColomboFort, ColomboFortPlatformTwo, null, now.AddMinutes(60)),
        Stop(2, Kollupitiya, KollupitiyaPlatform, now.AddMinutes(70), now.AddMinutes(71)),
        Stop(3, MountLavinia, MountLaviniaPlatform, now.AddMinutes(90), null),
    ];

    private SmartRailApiFactory NewFactory(DateTimeOffset now) =>
        new(
            _postgres.ConnectionString,
            configureServices: services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(now));
            });

    private static string JourneyRoute(Guid journeyId) =>
        $"{ApiRoutes.BasePath}/public/journeys/{journeyId}";

    private static async Task StartAsync(SmartRailApiFactory factory)
    {
        using var client = factory.CreateApiClient();
        using var response = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<JsonDocument> GetOkAsync(HttpClient client, string route)
    {
        using var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static async Task<ReadResponse> GetAsync(HttpClient client, string route)
    {
        using var response = await client.GetAsync(route);

        return new ReadResponse(
            response.StatusCode,
            response.Content.Headers.ContentType?.MediaType,
            await response.Content.ReadAsStringAsync(),
            response.Headers.CacheControl?.ToString(),
            response.Headers.TryGetValues("Set-Cookie", out _));
    }

    /// <summary>Creates a journey's live state through the real generation announcement.</summary>
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

    private static async Task<HttpResponseMessage> PostCancellationAsync(HttpClient client, Guid journeyId)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{ApiRoutes.BasePath}/operations/journeys/{journeyId}/cancellation");
        request.WithStaffToken(TestTokens.Create(ControllerSubject, Roles.NetworkController));

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostCorrectionAsync(HttpClient client, Guid changeId)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{ApiRoutes.BasePath}/operations/changes/{changeId}/corrections");
        request.WithStaffToken(TestTokens.Create(ControllerSubject, Roles.NetworkController));

        return await client.SendAsync(request);
    }

    private static JourneyStopDto Stop(
        int sequence,
        Guid stationId,
        Guid? platformId,
        DateTimeOffset? arrival,
        DateTimeOffset? departure,
        bool isCalling = true) =>
        new(sequence, stationId, isCalling, arrival, departure, platformId);

    private static bool IsNullOrAbsent(JsonElement element, string property) =>
        !element.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null;

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

    /// <summary>Waits until every request of the test has its one request-log entry.</summary>
    private static async Task WaitForRequestLogsAsync(LogCapture logs, int expected)
    {
        var deadline = DateTimeOffset.UtcNow.Add(Timeout);

        while (logs.Entries.Count(entry => entry.Message.Contains("responded", StringComparison.Ordinal)) < expected
               && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }
    }

    private async Task<long> MaxAuditPositionAsync() =>
        Assert.Single(await _postgres.QueryAsync(
            "SELECT coalesce(max(chain_position), 0) FROM audit.entries",
            reader => reader.GetInt64(0)));

    private async Task<int> CountAsync(string sql)
    {
        var count = Assert.Single(await _postgres.QueryAsync(sql, reader => reader.GetInt64(0)));
        return (int)count;
    }

    /// <summary>Removes every row the test created for its journeys, then the audit entries it appended.</summary>
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

    /// <summary>One response read in full, for the header and body assertions.</summary>
    private sealed record ReadResponse(
        HttpStatusCode Status,
        string? MediaType,
        string Body,
        string? CacheControl,
        bool HasSetCookie);

    /// <summary>A clock frozen at one instant, so "now" and the seeded timetable line up exactly.</summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
