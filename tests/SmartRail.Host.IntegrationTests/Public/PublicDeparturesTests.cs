using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartRail.Host.Http;
using SmartRail.Host.IntegrationTests.Infrastructure;

namespace SmartRail.Host.IntegrationTests.Public;

/// <summary>
/// The passenger departure endpoints against the real seeded network and timetable (tasks 2.1 and
/// 2.2): the direct journey with its origin times and platform, the interchange's two lines, and the
/// freshness and privacy of the response.
/// </summary>
/// <remarks>
/// The Host runs with a fixed clock at noon in the railway's time zone on the day's date, so the
/// seeded departures around it are the same whenever the suite runs.
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

    private static readonly Guid MountLavinia = Guid.Parse("c078a9aa-2f80-7422-a65b-bb7313795bd5");

    private static readonly Guid Ragama = Guid.Parse("0aca0135-24c9-7ab1-9843-3c3c79585d22");

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

    private static string? LineName(JsonElement item) =>
        item.GetProperty("journey").GetProperty("line").GetProperty("name").GetString();

    private static int SequenceOf(JsonElement journey, Guid stationId) =>
        journey.GetProperty("stops")
            .EnumerateArray()
            .Single(stop => stop.GetProperty("station").GetProperty("id").GetGuid() == stationId)
            .GetProperty("sequence")
            .GetInt32();

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
