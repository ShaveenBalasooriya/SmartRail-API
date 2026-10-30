using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
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
/// The Guard's live stream (task 4.3, live-event-streams spec "Guard stream scoped to own
/// journeys", "Resume from last event" and "No personal data in streams"): a guard receives a change
/// on an assigned journey once, in the public frame format; another guard's journey never reaches
/// the connection; a controller or auditor token is forbidden and no token is unauthorized; a
/// reconnecting guard is caught up on the missed events of their own journeys in order and exactly
/// once; and the response is uncacheable and sets no cookie (design D2, D4, D7).
/// </summary>
/// <remarks>
/// The Host runs with a fixed clock at noon in the railway's time zone, as the neighbouring public
/// tests do, so the seeded timetable and the journeys line up whenever the suite runs. The heartbeat
/// is shortened to one second through <c>Distribution:StreamHeartbeatSeconds</c> so a quiet stream
/// can be observed without waiting fifteen. Every read has its own timeout, so a stream that never
/// sends fails the test rather than hanging it.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class GuardStreamTests
{
    private const string GuardSubject = "guard.ishara";

    /// <summary>
    /// The subject of the second guard's token: a token's staff account comes from its subject and
    /// its role from the group it names, so this subject holding the guard group is a guard with a
    /// different staff id (the seeded accounts hold no second guard).
    /// </summary>
    private const string SecondGuardSubject = "auditor.wasantha";

    private const string ControllerSubject = "controller.mahesh";

    /// <summary>The seeded guard account (seed/identity/staff.json), which owns the journeys below.</summary>
    private static readonly Guid GuardStaffId = Guid.Parse("01a15874-e69d-73b2-b24d-d85d22fdead2");

    /// <summary>The staff account the other guard's journeys are assigned to (seed/identity/staff.json).</summary>
    private static readonly Guid SecondGuardStaffId = Guid.Parse("01a15874-e6a0-7fd3-8c46-34bd36e8f16d");

    private static readonly Guid ColomboFort = Guid.Parse("04624d0f-9410-72e3-967f-501ed71f2441");
    private static readonly Guid Kollupitiya = Guid.Parse("dc1ed19b-f522-70a9-ae61-15e47102a42f");
    private static readonly Guid MountLavinia = Guid.Parse("c078a9aa-2f80-7422-a65b-bb7313795bd5");

    /// <summary>The seeded Coastal Line (seed/network/network.json), which serves the stations above.</summary>
    private static readonly Guid CoastalLine = Guid.Parse("f8493f1c-67b5-76e1-acb6-215d4d9f60ae");

    private static readonly Guid KollupitiyaPlatform = Guid.Parse("e86062e1-aa04-7673-b2dc-51892f077a58");
    private static readonly Guid MountLaviniaPlatform = Guid.Parse("579f8b1e-e3fe-789b-9ba3-20aae4342df3");
    private static readonly Guid ColomboFortPlatformTwo = Guid.Parse("61e67fba-522e-73bf-beab-5cb90e861c86");

    private const string StreamRoute = $"{ApiRoutes.BasePath}/guard/stream";

    /// <summary>How long one event may take to reach a connected client.</summary>
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(20);

    /// <summary>How long a quiet stream is watched for stray events and for its heartbeat.</summary>
    private static readonly TimeSpan QuietWindow = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private readonly PostgresFixture _postgres;

    public GuardStreamTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task A_guard_receives_an_event_for_an_assigned_journey_once()
    {
        var today = RailwayToday();
        var now = SafeNow(today);
        using var factory = NewFactory(now);
        var journeyId = await CreateJourneyAsync(factory, today, now, GuardStaffId);
        var auditBaseline = await MaxAuditPositionAsync();

        try
        {
            using var client = factory.CreateClient(ClientOptions());

            using var stream = await EventStream.OpenAsync(client, GuardToken());

            using var published = await PostDelayAsync(client, journeyId, fromStopSequence: 2, minutes: 15);
            Assert.Equal(HttpStatusCode.Created, published.StatusCode);

            var position = await StreamPositionAsync(journeyId);
            var read = await stream.ReadAsync(EventTimeout, stopAtFirstEvent: true);
            Assert.True(
                read.Events.Count == 1,
                $"the guard's stream received {read.Events.Count} events and {read.Comments} comments "
                + $"in {EventTimeout}; outbox: {await DeliveryStateAsync(journeyId)}; "
                + $"bus log: {BusLog(factory.Logs)}");

            var received = read.Events[0];
            Assert.Equal(position.ToString(CultureInfo.InvariantCulture), received.Id);
            Assert.Equal("delay_published", received.EventType);

            // The payload is the same live view the public detail returns: the frame carries the
            // journey and nothing about the guard.
            using (var payload = JsonDocument.Parse(received.Data))
            {
                Assert.Equal(journeyId, payload.RootElement.GetProperty("journeyId").GetGuid());
            }

            Assert.DoesNotContain(GuardStaffId.ToString(), received.Data, StringComparison.OrdinalIgnoreCase);

            // Nothing is delivered a second time, and the quiet stream still sends heartbeats.
            var quiet = await stream.ReadAsync(QuietWindow, stopAtFirstEvent: false);
            Assert.Empty(quiet.Events);
            Assert.True(quiet.Comments > 0, "a quiet guard stream must still send heartbeats.");
        }
        finally
        {
            await CleanUpAsync(journeyId, auditBaseline);
        }
    }

    [Fact]
    public async Task A_guard_does_not_receive_an_event_for_another_guards_journey()
    {
        var today = RailwayToday();
        var now = SafeNow(today);
        using var factory = NewFactory(now);
        var journeyId = await CreateJourneyAsync(factory, today, now, SecondGuardStaffId);
        var auditBaseline = await MaxAuditPositionAsync();

        try
        {
            using var client = factory.CreateClient(ClientOptions());

            // The connection exists before the change is committed, so "not received" is meaningful.
            using var stream = await EventStream.OpenAsync(client, GuardToken());

            using var published = await PostDelayAsync(client, journeyId, fromStopSequence: 2, minutes: 15);
            Assert.Equal(HttpStatusCode.Created, published.StatusCode);

            // The change is real and committed; the other guard's connection simply never sees it.
            Assert.Single(await StreamPositionsAsync(journeyId));

            var read = await stream.ReadAsync(QuietWindow, stopAtFirstEvent: false);
            Assert.Empty(read.Events);
            Assert.True(read.Comments > 0, "a quiet guard stream must still send heartbeats.");
        }
        finally
        {
            await CleanUpAsync(journeyId, auditBaseline);
        }
    }

    [Theory]
    [InlineData(ControllerSubject, Roles.NetworkController)]
    [InlineData("auditor.wasantha", Roles.Auditor)]
    public async Task A_staff_member_who_is_not_a_guard_is_forbidden(string subject, string role)
    {
        var today = RailwayToday();
        var now = SafeNow(today);
        using var factory = NewFactory(now);
        await StartAsync(factory);
        using var client = factory.CreateApiClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, StreamRoute)
            .WithStaffToken(TestTokens.Create(subject, role));
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_request_with_no_token_is_unauthorized()
    {
        var today = RailwayToday();
        var now = SafeNow(today);
        using var factory = NewFactory(now);
        await StartAsync(factory);
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync(StreamRoute);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// A guard that reconnects with the last position it saw is first sent the missed changes of
    /// their own journeys, in stream-position order and exactly once, and then continues live
    /// (live-event-streams spec, "Resume from last event"; design D4). The missed changes are
    /// committed while no stream is connected, so only the catch-up read can deliver them.
    /// </summary>
    [Fact]
    public async Task A_reconnecting_guard_stream_receives_the_missed_own_journey_events_in_order()
    {
        var today = RailwayToday();
        var now = SafeNow(today);
        using var factory = NewFactory(now);
        var journeyId = await CreateJourneyAsync(factory, today, now, GuardStaffId);
        var auditBaseline = await MaxAuditPositionAsync();

        try
        {
            using var client = factory.CreateClient(ClientOptions());

            // The change the guard has already received. Its position is a real one, so the resume
            // is a catch-up rather than a reset.
            using var seen = await PostDelayAsync(client, journeyId, fromStopSequence: 2, minutes: 5);
            Assert.Equal(HttpStatusCode.Created, seen.StatusCode);

            var lastSeen = Assert.Single(await StreamPositionsAsync(journeyId));

            // Two changes committed while the guard's app was disconnected.
            using var first = await PostDelayAsync(client, journeyId, fromStopSequence: 2, minutes: 10);
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
            using var second = await PostDelayAsync(client, journeyId, fromStopSequence: 3, minutes: 15);
            Assert.Equal(HttpStatusCode.Created, second.StatusCode);

            var positions = await StreamPositionsAsync(journeyId);
            Assert.Equal(3, positions.Count);

            using var stream = await EventStream.OpenAsync(
                client,
                GuardToken(),
                lastEventId: lastSeen.ToString(CultureInfo.InvariantCulture));

            var caughtUp = await stream.ReadAsync(EventTimeout, stopAtFirstEvent: false, stopAfterEvents: 2);
            Assert.True(
                caughtUp.Events.Count == 2,
                $"the resumed guard stream received {caughtUp.Events.Count} catch-up events and "
                + $"{caughtUp.Comments} comments in {EventTimeout}; "
                + $"outbox: {await DeliveryStateAsync(journeyId)}; bus log: {BusLog(factory.Logs)}");

            // Both missed events, in position order, with the IDs the stream sent.
            Assert.Equal(
                positions.Skip(1).Select(position => position.ToString(CultureInfo.InvariantCulture)),
                caughtUp.Events.Select(frame => frame.Id));
            Assert.All(caughtUp.Events, frame => Assert.Equal("delay_published", frame.EventType));

            // No duplicate follows the join, and the next change arrives live.
            using var third = await PostDelayAsync(client, journeyId, fromStopSequence: 3, minutes: 20);
            Assert.Equal(HttpStatusCode.Created, third.StatusCode);

            var livePositions = await StreamPositionsAsync(journeyId);
            var live = await stream.ReadAsync(EventTimeout, stopAtFirstEvent: true);
            Assert.True(
                live.Events.Count == 1,
                $"the resumed guard stream received {live.Events.Count} events after the catch-up in "
                + $"{EventTimeout}; outbox: {await DeliveryStateAsync(journeyId)}; "
                + $"bus log: {BusLog(factory.Logs)}");

            var liveEvent = Assert.Single(live.Events);
            Assert.Equal(livePositions[^1].ToString(CultureInfo.InvariantCulture), liveEvent.Id);
            Assert.Equal("delay_published", liveEvent.EventType);

            var quiet = await stream.ReadAsync(QuietWindow, stopAtFirstEvent: false);
            Assert.Empty(quiet.Events);
        }
        finally
        {
            await CleanUpAsync(journeyId, auditBaseline);
        }
    }

    private static WebApplicationFactoryClientOptions ClientOptions() =>
        new()
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        };

    private static string GuardToken() => TestTokens.Create(GuardSubject, Roles.Guard);

    private SmartRailApiFactory NewFactory(DateTimeOffset now) =>
        new(
            _postgres.ConnectionString,
            configureServices: services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(now));
            },
            settings: new Dictionary<string, string>
            {
                // Short enough to observe in one test, while the production default stays 15.
                ["Distribution:StreamHeartbeatSeconds"] = "1",
            });

    /// <summary>Creates a journey's live state through the real generation announcement.</summary>
    private async Task<Guid> CreateJourneyAsync(
        SmartRailApiFactory factory,
        DateOnly serviceDate,
        DateTimeOffset now,
        Guid guardStaffId)
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
                guardStaffId,
                [
                    new JourneyStopDto(1, ColomboFort, true, null, now.AddMinutes(60), ColomboFortPlatformTwo),
                    new JourneyStopDto(
                        2,
                        Kollupitiya,
                        true,
                        now.AddMinutes(70),
                        now.AddMinutes(71),
                        KollupitiyaPlatform),
                    new JourneyStopDto(3, MountLavinia, true, now.AddMinutes(90), null, MountLaviniaPlatform),
                ]),
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

    private static async Task StartAsync(SmartRailApiFactory factory)
    {
        using var client = factory.CreateApiClient();
        using var response = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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

    private async Task<long> StreamPositionAsync(Guid journeyId) =>
        Assert.Single(await StreamPositionsAsync(journeyId));

    /// <summary>Every stored event position of the journey, in stream order.</summary>
    private async Task<List<long>> StreamPositionsAsync(Guid journeyId) =>
        await _postgres.QueryAsync(
            $"""
            SELECT stream_position
            FROM operations.journey_events
            WHERE journey_id = '{journeyId}'
            ORDER BY stream_position
            """,
            reader => reader.GetInt64(0));

    /// <summary>
    /// The delivery state of the journey's announcements, so a stream that received nothing says
    /// whether the bus ever offered it.
    /// </summary>
    private async Task<string> DeliveryStateAsync(Guid journeyId)
    {
        var rows = await _postgres.QueryAsync(
            $"""
            SELECT coalesce(string_agg(
                       d.consumer || '=' || d.status || '#' || d.attempt_count
                       || '@' || coalesce(d.next_attempt_at::text, '-'), ', '), 'none')
            FROM operations.outbox_deliveries d
            JOIN operations.outbox_messages m ON m.id = d.message_id
            WHERE m.ordering_key = '{journeyId}'
            """,
            reader => reader.GetString(0));

        return string.Join(" | ", rows);
    }

    /// <summary>The dispatcher's own last words, so a stream that received nothing says why.</summary>
    private static string BusLog(LogCapture logs) =>
        string.Join(
            " || ",
            logs.Entries
                .Where(entry => entry.Category.Contains("Events.", StringComparison.Ordinal))
                .TakeLast(4)
                .Select(entry => entry.ToString()));

    private async Task<int> CountAsync(string sql)
    {
        var count = Assert.Single(await _postgres.QueryAsync(sql, reader => reader.GetInt64(0)));
        return (int)count;
    }

    private async Task<long> MaxAuditPositionAsync() =>
        Assert.Single(await _postgres.QueryAsync(
            "SELECT coalesce(max(chain_position), 0) FROM audit.entries",
            reader => reader.GetInt64(0)));

    /// <summary>Removes every row the test created for its journey, then the audit entries it appended.</summary>
    private async Task CleanUpAsync(Guid journeyId, long auditBaseline)
    {
        // The delivery rows reference the message by identifier alone, so they go first: a pending
        // row whose message had been deleted would stop the operations outbox from delivering.
        await _postgres.ExecuteAsync(
            $"""
            DELETE FROM operations.outbox_deliveries
            WHERE message_id IN (
                SELECT id FROM operations.outbox_messages WHERE ordering_key = '{journeyId}')
            """);
        await _postgres.ExecuteAsync(
            $"DELETE FROM operations.status_changes WHERE journey_id = '{journeyId}'");
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

    private static DateOnly RailwayToday() => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTime(
            DateTimeOffset.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Colombo")).DateTime);

    /// <summary>
    /// Noon in the railway's time zone on the given day, as UTC: safely inside the day, so the
    /// seeded timetable and the journey line up whenever the suite runs.
    /// </summary>
    private static DateTimeOffset SafeNow(DateOnly serviceDate) =>
        new DateTimeOffset(
            serviceDate.ToDateTime(new TimeOnly(12, 0)),
            TimeSpan.FromHours(5.5)).ToUniversalTime();

    /// <summary>One open Server-Sent Events response, read frame by frame.</summary>
    private sealed class EventStream : IDisposable
    {
        private readonly HttpResponseMessage response;
        private readonly StreamReader reader;

        private EventStream(HttpResponseMessage response, StreamReader reader)
        {
            this.response = response;
            this.reader = reader;
        }

        /// <summary>
        /// Opens the guard stream with the guard's token and checks the conventions the response
        /// shares with the public stream (design D7). A resume sends the standard
        /// <c>Last-Event-ID</c> header, which is the only way a position is passed.
        /// </summary>
        public static async Task<EventStream> OpenAsync(
            HttpClient client,
            string token,
            string? lastEventId = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, StreamRoute).WithStaffToken(token);

            if (lastEventId is not null)
            {
                request.Headers.TryAddWithoutValidation("Last-Event-ID", lastEventId);
            }

            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
            Assert.False(response.Headers.TryGetValues("Set-Cookie", out _));

            return new EventStream(
                response,
                new StreamReader(await response.Content.ReadAsStreamAsync(), Encoding.UTF8));
        }

        /// <summary>
        /// Reads frames until <paramref name="window"/> elapses, until the first event when
        /// <paramref name="stopAtFirstEvent"/> is set, or until <paramref name="stopAfterEvents"/>
        /// events have arrived. A stream that sends nothing is not an error here; the caller asserts on
        /// what came back, so a quiet stream never hangs the test.
        /// </summary>
        public async Task<StreamRead> ReadAsync(
            TimeSpan window,
            bool stopAtFirstEvent,
            int? stopAfterEvents = null)
        {
            using var timeout = new CancellationTokenSource(window);

            var events = new List<SseFrame>();
            var comments = 0;
            string? id = null;
            string? eventType = null;
            var data = new StringBuilder();

            try
            {
                while (true)
                {
                    var line = await reader.ReadLineAsync(timeout.Token);

                    if (line is null)
                    {
                        break;
                    }

                    if (line.StartsWith(':'))
                    {
                        comments++;
                        continue;
                    }

                    if (line.Length == 0)
                    {
                        if (eventType is not null)
                        {
                            events.Add(new SseFrame(id, eventType, data.ToString()));

                            if (stopAtFirstEvent || (stopAfterEvents is { } until && events.Count >= until))
                            {
                                break;
                            }
                        }

                        id = null;
                        eventType = null;
                        data.Clear();
                        continue;
                    }

                    if (line.StartsWith("id: ", StringComparison.Ordinal))
                    {
                        id = line[4..];
                    }
                    else if (line.StartsWith("event: ", StringComparison.Ordinal))
                    {
                        eventType = line[7..];
                    }
                    else if (line.StartsWith("data: ", StringComparison.Ordinal))
                    {
                        data.Append(line[6..]);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // The window elapsed, or the stream was closed under the reader.
            }

            return new StreamRead(events, comments);
        }

        public void Dispose()
        {
            reader.Dispose();
            response.Dispose();
        }
    }

    /// <summary>What one window of reading produced.</summary>
    private sealed record StreamRead(IReadOnlyList<SseFrame> Events, int Comments);

    /// <summary>One parsed event frame.</summary>
    private sealed record SseFrame(string? Id, string? EventType, string Data);

    /// <summary>A clock frozen at one instant, so "now" and the journey's times line up exactly.</summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
