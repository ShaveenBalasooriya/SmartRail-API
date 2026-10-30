using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartRail.Host.Http;
using SmartRail.Host.IntegrationTests.Infrastructure;
using SmartRail.RailNetwork.Contracts;
using SmartRail.RailNetwork.Infrastructure.Persistence;
using SmartRail.SharedKernel.Application.Abstractions;
using SmartRail.SharedKernel.Application.Events;

namespace SmartRail.Host.IntegrationTests.Public;

/// <summary>
/// The passenger departure endpoints against the real seeded network and timetable (tasks 2.1 and
/// 2.2): the direct journey with its origin times and platform, the station passed without calling,
/// the wrong direction, the empty answer across two lines, the interchange's two lines, the window
/// bounds, the unknown station, what still counts as upcoming for a train that just left, a
/// cancelled one and one ended early, and the freshness and privacy of the response.
/// </summary>
/// <remarks>
/// The Host runs with a fixed clock at noon in the railway's time zone on the day's date, so the
/// seeded departures around it are the same whenever the suite runs. A journey the seed cannot
/// express (a skipped station, the other direction, a live state) is created through the real
/// generation announcement, and its state is driven through the real Guard and controller endpoints.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class PublicDeparturesTests
{
    private const string GuardSubject = "guard.ishara";

    private const string ControllerSubject = "controller.mahesh";

    /// <summary>The seeded guard account (seed/identity/staff.json), which owns every seeded service.</summary>
    private static readonly Guid GuardStaffId = Guid.Parse("01a15874-e69d-73b2-b24d-d85d22fdead2");

    /// <summary>The seeded controller account; neither its identifier nor its name may reach a public response.</summary>
    private static readonly Guid ControllerStaffId = Guid.Parse("01a15874-e69c-70da-a689-76a7268ed330");

    /// <summary>The interchange station on the seeded network (seed/network/network.json).</summary>
    private static readonly Guid ColomboFort = Guid.Parse("04624d0f-9410-72e3-967f-501ed71f2441");

    private static readonly Guid Kollupitiya = Guid.Parse("dc1ed19b-f522-70a9-ae61-15e47102a42f");
    private static readonly Guid Bambalapitiya = Guid.Parse("6f818201-ebf9-7374-bbad-74adbab9df38");
    private static readonly Guid Wellawatte = Guid.Parse("f9df5210-ee64-7a25-bddb-2874488f113d");
    private static readonly Guid MountLavinia = Guid.Parse("c078a9aa-2f80-7422-a65b-bb7313795bd5");
    private static readonly Guid Maradana = Guid.Parse("0059a119-bff4-7696-8cc0-5f44b3631aef");
    private static readonly Guid Ragama = Guid.Parse("0aca0135-24c9-7ab1-9843-3c3c79585d22");

    private static readonly Guid ColomboFortPlatform = Guid.Parse("61e67fba-522e-73bf-beab-5cb90e861c86");
    private static readonly Guid KollupitiyaPlatform = Guid.Parse("e86062e1-aa04-7673-b2dc-51892f077a58");
    private static readonly Guid BambalapitiyaPlatform = Guid.Parse("8f4b8be6-fea4-7c57-bd1d-64638e6273af");
    private static readonly Guid WellawattePlatform = Guid.Parse("adfe3193-4394-7926-aa11-8ba3870656dd");
    private static readonly Guid MountLaviniaPlatform = Guid.Parse("579f8b1e-e3fe-789b-9ba3-20aae4342df3");

    /// <summary>The seeded Coastal Line (seed/network/network.json), which serves the stations above.</summary>
    private static readonly Guid CoastalLine = Guid.Parse("f8493f1c-67b5-76e1-acb6-215d4d9f60ae");

    private static readonly string GuardDepartureLogsRoute = $"{ApiRoutes.BasePath}/guard/departure-logs";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private readonly PostgresFixture _postgres;

    public PublicDeparturesTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task An_anonymous_request_lists_the_seeded_direct_trains_with_their_origin_times()
    {
        var now = SafeNow(RailwayToday());
        using var factory = NewFactory(now);
        using var client = factory.CreateApiClient();

        using var document = await WaitForSeededDeparturesAsync(client);
        var items = document.RootElement.GetProperty("data").EnumerateArray().ToArray();

        Assert.NotEmpty(items);

        foreach (var item in items)
        {
            var journey = item.GetProperty("journey");
            var origin = item.GetProperty("origin");

            Assert.Equal("Coastal Line", journey.GetProperty("line").GetProperty("name").GetString());
            Assert.Equal("scheduled", journey.GetProperty("status").GetString());
            Assert.Equal(ColomboFort, origin.GetProperty("station").GetProperty("id").GetGuid());
            Assert.True(origin.GetProperty("scheduledDeparture").GetDateTimeOffset() >= now);
            Assert.NotEqual(JsonValueKind.Null, origin.GetProperty("expectedDeparture").ValueKind);
            Assert.False(string.IsNullOrWhiteSpace(origin.GetProperty("platform").GetProperty("number").GetString()));
            Assert.True(SequenceOf(journey, ColomboFort) < SequenceOf(journey, Kollupitiya));

            // Every Coastal departure from Colombo Fort runs to the line's final calling stop.
            Assert.Equal(MountLavinia, item.GetProperty("destination").GetProperty("id").GetGuid());
            Assert.Equal("Mount Lavinia", item.GetProperty("destination").GetProperty("name").GetString());
        }

        // Ordered by the expected departure at the origin.
        var departures = items
            .Select(item => item.GetProperty("origin").GetProperty("expectedDeparture").GetDateTimeOffset())
            .ToArray();

        Assert.Equal(departures.Order().ToArray(), departures);
    }

    [Fact]
    public async Task A_journey_that_passes_the_destination_without_calling_is_not_listed()
    {
        var today = RailwayToday();
        var now = SafeNow(today);
        using var factory = NewFactory(now);
        var journeyId = await CreateJourneyAsync(
            factory,
            today,
            [
                Stop(1, ColomboFort, ColomboFortPlatform, null, now.AddMinutes(60)),
                Stop(2, Kollupitiya, KollupitiyaPlatform, now.AddMinutes(70), now.AddMinutes(71)),
                Stop(3, Bambalapitiya, null, null, null, isCalling: false),
                Stop(4, Wellawatte, WellawattePlatform, now.AddMinutes(85), now.AddMinutes(86)),
                Stop(5, MountLavinia, MountLaviniaPlatform, now.AddMinutes(95), null),
            ]);

        var auditBaseline = await MaxAuditPositionAsync();

        try
        {
            using var client = factory.CreateApiClient();
            using var document = await GetOkAsync(client, DeparturesRoute(ColomboFort, Bambalapitiya));
            var items = document.RootElement.GetProperty("data").EnumerateArray().ToArray();

            // The seeded Coastal trains do call at Bambalapitiya, so the list is not empty; the
            // journey that only passes it is the one that must not be there.
            Assert.NotEmpty(items);
            Assert.DoesNotContain(journeyId, JourneyIds(items));
            Assert.All(items, item => Assert.True(
                HasCallingStop(item.GetProperty("journey"), Bambalapitiya),
                "A listed journey must call at the destination station."));
        }
        finally
        {
            await CleanUpAsync([journeyId], auditBaseline);
        }
    }

    [Fact]
    public async Task A_journey_that_reaches_the_destination_first_is_not_listed()
    {
        var today = RailwayToday();
        var now = SafeNow(today);
        using var factory = NewFactory(now);
        var journeyId = await CreateJourneyAsync(
            factory,
            today,
            [
                Stop(1, Wellawatte, WellawattePlatform, null, now.AddMinutes(60)),
                Stop(2, Bambalapitiya, BambalapitiyaPlatform, now.AddMinutes(70), now.AddMinutes(71)),
                Stop(3, Kollupitiya, KollupitiyaPlatform, now.AddMinutes(80), now.AddMinutes(81)),
                Stop(4, ColomboFort, ColomboFortPlatform, now.AddMinutes(90), null),
            ]);

        var auditBaseline = await MaxAuditPositionAsync();

        try
        {
            using var client = factory.CreateApiClient();
            using var document = await GetOkAsync(client, DeparturesRoute(ColomboFort, Kollupitiya));
            var items = document.RootElement.GetProperty("data").EnumerateArray().ToArray();

            Assert.NotEmpty(items);
            Assert.DoesNotContain(journeyId, JourneyIds(items));
            Assert.All(items, item => Assert.True(
                SequenceOf(item.GetProperty("journey"), ColomboFort)
                    < SequenceOf(item.GetProperty("journey"), Kollupitiya),
                "Only a journey that reaches the destination after the origin may be listed."));
        }
        finally
        {
            await CleanUpAsync([journeyId], auditBaseline);
        }
    }

    [Fact]
    public async Task Stations_on_different_lines_answer_an_empty_list_without_a_change_of_line()
    {
        using var factory = NewFactory(SafeNow(RailwayToday()));
        using var client = factory.CreateApiClient();
        await StartAsync(factory);

        using var document = await GetOkAsync(client, DeparturesRoute(Kollupitiya, Maradana));

        Assert.Equal(0, document.RootElement.GetProperty("data").GetArrayLength());
    }

    [Fact]
    public async Task The_interchange_lists_the_onward_departures_of_both_lines()
    {
        var now = SafeNow(RailwayToday());
        using var factory = NewFactory(now);
        using var client = factory.CreateApiClient();
        await WaitForSeededDeparturesAsync(client);

        using var document = await GetOkAsync(client, StationDeparturesRoute(ColomboFort));
        var items = document.RootElement.GetProperty("data").EnumerateArray().ToArray();

        // At noon the next two hours hold one Coastal and one Main departure from the interchange.
        Assert.Equal(2, items.Length);

        var coastal = Assert.Single(items, item => LineName(item) == "Coastal Line");
        var main = Assert.Single(items, item => LineName(item) == "Main Line");

        Assert.Equal(MountLavinia, coastal.GetProperty("destination").GetProperty("id").GetGuid());
        Assert.Equal("Mount Lavinia", coastal.GetProperty("destination").GetProperty("name").GetString());
        Assert.Equal(Ragama, main.GetProperty("destination").GetProperty("id").GetGuid());
        Assert.Equal("Ragama", main.GetProperty("destination").GetProperty("name").GetString());

        Assert.All(items, item =>
        {
            Assert.Equal(ColomboFort, item.GetProperty("origin").GetProperty("station").GetProperty("id").GetGuid());
            Assert.False(string.IsNullOrWhiteSpace(LineName(item)));
            Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("destination").GetProperty("name").GetString()));
        });
    }

    [Fact]
    public async Task The_window_is_two_hours_by_default_and_at_most_six()
    {
        var now = SafeNow(RailwayToday());
        using var factory = NewFactory(now);
        using var client = factory.CreateApiClient();
        await WaitForSeededDeparturesAsync(client);

        var tooLarge = await GetAsync(client, StationDeparturesRoute(ColomboFort, 720));
        Assert.Equal(HttpStatusCode.BadRequest, tooLarge.Status);
        Assert.Equal("application/problem+json", tooLarge.MediaType);
        Assert.Contains("windowMinutes", tooLarge.Body, StringComparison.Ordinal);

        var notPositive = await GetAsync(client, StationDeparturesRoute(ColomboFort, 0));
        Assert.Equal(HttpStatusCode.BadRequest, notPositive.Status);
        Assert.Contains("windowMinutes", notPositive.Body, StringComparison.Ordinal);

        using var byDefault = await GetOkAsync(client, StationDeparturesRoute(ColomboFort));
        using var byTwoHours = await GetOkAsync(client, StationDeparturesRoute(ColomboFort, 120));

        Assert.Equal(
            JourneyIds(byTwoHours.RootElement.GetProperty("data").EnumerateArray().ToArray()),
            JourneyIds(byDefault.RootElement.GetProperty("data").EnumerateArray().ToArray()));
    }

    [Fact]
    public async Task An_unknown_or_missing_station_is_refused()
    {
        using var factory = NewFactory(SafeNow(RailwayToday()));
        using var client = factory.CreateApiClient();
        await StartAsync(factory);

        var unknown = Guid.CreateVersion7();

        var unknownFrom = await GetAsync(client, DeparturesRoute(unknown, Kollupitiya));
        Assert.Equal(HttpStatusCode.NotFound, unknownFrom.Status);
        Assert.Equal("application/problem+json", unknownFrom.MediaType);

        var unknownStation = await GetAsync(client, StationDeparturesRoute(unknown));
        Assert.Equal(HttpStatusCode.NotFound, unknownStation.Status);

        var missingFrom = await GetAsync(
            client,
            $"{ApiRoutes.BasePath}/public/departures?to={Kollupitiya}");
        Assert.Equal(HttpStatusCode.BadRequest, missingFrom.Status);
        Assert.Contains("from", missingFrom.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_train_that_departed_a_minute_ago_is_still_listed_with_its_actual_departure()
    {
        var today = RailwayToday();
        var now = SafeNow(today);
        using var factory = NewFactory(now);
        var journeyId = await CreateJourneyAsync(
            factory,
            today,
            [
                Stop(1, ColomboFort, ColomboFortPlatform, null, now.AddMinutes(-30)),
                Stop(2, Kollupitiya, KollupitiyaPlatform, now.AddMinutes(-10), now.AddMinutes(-1)),
                Stop(3, Bambalapitiya, BambalapitiyaPlatform, now.AddMinutes(5), now.AddMinutes(10)),
                Stop(4, Wellawatte, WellawattePlatform, now.AddMinutes(20), null),
            ]);

        var auditBaseline = await MaxAuditPositionAsync();

        try
        {
            using var client = factory.CreateApiClient();

            using var logged = await PostDepartureAsync(client, journeyId, stopSequence: 2, now.AddMinutes(-1));
            Assert.Equal(HttpStatusCode.Created, logged.StatusCode);

            using var document = await GetOkAsync(client, StationDeparturesRoute(Kollupitiya));
            var item = Assert.Single(
                document.RootElement.GetProperty("data").EnumerateArray(),
                candidate => JourneyIdOf(candidate) == journeyId);

            var journey = item.GetProperty("journey");
            var origin = item.GetProperty("origin");

            Assert.Equal("running", journey.GetProperty("status").GetString());
            Assert.Equal(now.AddMinutes(-1), origin.GetProperty("actualDeparture").GetDateTimeOffset());
            Assert.Equal(now.AddMinutes(-1), origin.GetProperty("expectedDeparture").GetDateTimeOffset());
            Assert.Equal("1", origin.GetProperty("platform").GetProperty("number").GetString());
            Assert.Equal(0, item.GetProperty("minutesLate").GetInt32());
        }
        finally
        {
            await CleanUpAsync([journeyId], auditBaseline);
        }
    }

    [Fact]
    public async Task A_cancelled_train_five_minutes_past_its_scheduled_departure_is_still_listed()
    {
        var today = RailwayToday();
        var now = SafeNow(today);
        using var factory = NewFactory(now);
        var journeyId = await CreateJourneyAsync(
            factory,
            today,
            [
                Stop(1, ColomboFort, ColomboFortPlatform, null, now.AddMinutes(-5)),
                Stop(2, Kollupitiya, KollupitiyaPlatform, now.AddMinutes(3), now.AddMinutes(4)),
                Stop(3, MountLavinia, MountLaviniaPlatform, now.AddMinutes(15), null),
            ]);

        var auditBaseline = await MaxAuditPositionAsync();

        try
        {
            using var client = factory.CreateApiClient();

            using var cancelled = await PublishCancellationAsync(client, journeyId);
            Assert.Equal(HttpStatusCode.Created, cancelled.StatusCode);

            using var document = await GetOkAsync(client, StationDeparturesRoute(ColomboFort));
            var item = Assert.Single(
                document.RootElement.GetProperty("data").EnumerateArray(),
                candidate => JourneyIdOf(candidate) == journeyId);

            Assert.Equal("cancelled", item.GetProperty("journey").GetProperty("status").GetString());
            Assert.Equal(
                "automatically_published",
                item.GetProperty("journey").GetProperty("provenance").GetString());
            Assert.Equal(
                now.AddMinutes(-5),
                item.GetProperty("origin").GetProperty("scheduledDeparture").GetDateTimeOffset());
            Assert.True(IsNullOrAbsent(item.GetProperty("origin"), "expectedDeparture"));
            Assert.True(IsNullOrAbsent(item, "minutesLate"));
            Assert.Equal(MountLavinia, item.GetProperty("destination").GetProperty("id").GetGuid());
        }
        finally
        {
            await CleanUpAsync([journeyId], auditBaseline);
        }
    }

    [Fact]
    public async Task A_journey_ended_early_is_listed_as_terminated_after_its_last_served_stop()
    {
        var today = RailwayToday();
        var now = SafeNow(today);
        using var factory = NewFactory(now);
        var journeyId = await CreateJourneyAsync(
            factory,
            today,
            [
                Stop(1, ColomboFort, ColomboFortPlatform, null, now.AddMinutes(-30)),
                Stop(2, Kollupitiya, KollupitiyaPlatform, now.AddMinutes(-20), now.AddMinutes(-19)),
                Stop(3, Bambalapitiya, BambalapitiyaPlatform, now.AddMinutes(5), now.AddMinutes(10)),
                Stop(4, Wellawatte, WellawattePlatform, now.AddMinutes(20), null),
            ]);

        var auditBaseline = await MaxAuditPositionAsync();

        try
        {
            using var client = factory.CreateApiClient();

            using var logged = await PostDepartureAsync(client, journeyId, stopSequence: 1, now.AddMinutes(-30));
            Assert.Equal(HttpStatusCode.Created, logged.StatusCode);

            using var terminated = await PublishTerminationAsync(client, journeyId, lastStopSequence: 2);
            Assert.Equal(HttpStatusCode.Created, terminated.StatusCode);

            using var document = await GetOkAsync(client, StationDeparturesRoute(Bambalapitiya));
            var item = Assert.Single(
                document.RootElement.GetProperty("data").EnumerateArray(),
                candidate => JourneyIdOf(candidate) == journeyId);

            Assert.Equal("terminated", item.GetProperty("journey").GetProperty("status").GetString());
            Assert.Equal(
                now.AddMinutes(10),
                item.GetProperty("origin").GetProperty("scheduledDeparture").GetDateTimeOffset());
            Assert.True(IsNullOrAbsent(item.GetProperty("origin"), "expectedDeparture"));
            Assert.True(IsNullOrAbsent(item.GetProperty("origin"), "actualDeparture"));
            Assert.True(IsNullOrAbsent(item, "minutesLate"));
            Assert.Equal(Wellawatte, item.GetProperty("destination").GetProperty("id").GetGuid());
        }
        finally
        {
            await CleanUpAsync([journeyId], auditBaseline);
        }
    }

    [Fact]
    public async Task Every_item_is_fresh_the_response_sets_no_cookie_and_no_staff_data_is_exposed()
    {
        var now = SafeNow(RailwayToday());
        using var factory = NewFactory(now);
        using var client = factory.CreateApiClient();
        await WaitForSeededDeparturesAsync(client);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            DeparturesRoute(ColomboFort, Kollupitiya));
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out _));

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        Assert.True(root.TryGetProperty("generatedAt", out var generatedAt));
        Assert.Equal(now, generatedAt.GetDateTimeOffset());
        Assert.Equal(15, root.GetProperty("refreshAfterSeconds").GetInt32());

        var items = root.GetProperty("data").EnumerateArray().ToArray();
        Assert.NotEmpty(items);
        Assert.All(items, item => Assert.True(
            item.GetProperty("journey").TryGetProperty("lastUpdatedAt", out _),
            "Every item must carry the last-updated time of the live state it was built from."));

        // Neither staff identifiers nor staff names ever reach a public payload.
        Assert.DoesNotContain(GuardStaffId.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ControllerStaffId.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Ishara", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Mahesh", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(GuardSubject, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ControllerSubject, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("staff", body, StringComparison.OrdinalIgnoreCase);
    }

    private SmartRailApiFactory NewFactory(DateTimeOffset now) =>
        new(
            _postgres.ConnectionString,
            configureServices: services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(now));
            });

    private static string DeparturesRoute(Guid from, Guid to) =>
        $"{ApiRoutes.BasePath}/public/departures?from={from}&to={to}";

    private static string StationDeparturesRoute(Guid stationId, int? windowMinutes = null) =>
        windowMinutes is { } window
            ? $"{ApiRoutes.BasePath}/public/stations/{stationId}/departures"
                + $"?windowMinutes={window.ToString(CultureInfo.InvariantCulture)}"
            : $"{ApiRoutes.BasePath}/public/stations/{stationId}/departures";

    private static async Task StartAsync(SmartRailApiFactory factory)
    {
        using var client = factory.CreateApiClient();
        using var response = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// Reads the departures list once the start-up generation has produced the seeded journeys. The
    /// hosted service runs after <c>app.Run()</c>, so the first request may meet an empty board.
    /// </summary>
    private static async Task<JsonDocument> WaitForSeededDeparturesAsync(HttpClient client)
    {
        var route = DeparturesRoute(ColomboFort, Kollupitiya);
        var deadline = DateTimeOffset.UtcNow.Add(Timeout);

        while (true)
        {
            using var response = await client.GetAsync(route);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            if (document.RootElement.GetProperty("data").GetArrayLength() > 0)
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

    private static async Task<HttpResponseMessage> PostDepartureAsync(
        HttpClient client,
        Guid journeyId,
        int stopSequence,
        DateTimeOffset departedAt)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, GuardDepartureLogsRoute)
        {
            Content = JsonContent.Create(new
            {
                id = Guid.CreateVersion7(),
                journeyId,
                stopSequence,
                departedAt = departedAt.ToString("O", CultureInfo.InvariantCulture),
            }),
        };
        request.WithStaffToken(TestTokens.Create(GuardSubject, Roles.Guard));

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PublishCancellationAsync(HttpClient client, Guid journeyId)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{ApiRoutes.BasePath}/operations/journeys/{journeyId}/cancellation");
        request.WithStaffToken(TestTokens.Create(ControllerSubject, Roles.NetworkController));

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PublishTerminationAsync(
        HttpClient client,
        Guid journeyId,
        int lastStopSequence)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{ApiRoutes.BasePath}/operations/journeys/{journeyId}/termination")
        {
            Content = JsonContent.Create(new { lastStopSequence }),
        };
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

    private static Guid JourneyIdOf(JsonElement item) =>
        item.GetProperty("journey").GetProperty("journeyId").GetGuid();

    private static Guid[] JourneyIds(IEnumerable<JsonElement> items) =>
        [.. items.Select(JourneyIdOf)];

    private static string? LineName(JsonElement item) =>
        item.GetProperty("journey").GetProperty("line").GetProperty("name").GetString();

    private static int SequenceOf(JsonElement journey, Guid stationId) =>
        journey.GetProperty("stops")
            .EnumerateArray()
            .Single(stop => stop.GetProperty("station").GetProperty("id").GetGuid() == stationId)
            .GetProperty("sequence")
            .GetInt32();

    private static bool HasCallingStop(JsonElement journey, Guid stationId) =>
        journey.GetProperty("stops")
            .EnumerateArray()
            .Any(stop => stop.GetProperty("station").GetProperty("id").GetGuid() == stationId
                && stop.GetProperty("isCalling").GetBoolean());

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

    /// <summary>A clock frozen at one instant, so "now" and the seeded timetable line up exactly.</summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
