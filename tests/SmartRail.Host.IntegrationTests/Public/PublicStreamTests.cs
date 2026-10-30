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
/// The public live stream (tasks 4.1 and 4.2, live-event-streams spec "Public event stream",
/// "Heartbeat", "Resume from last event" and "Catch-up beyond the retained window"): a published
/// delay reaches a client subscribed to a station the journey calls at, once, with the stream
/// position as its ID, the event type and the same live view the journey detail returns; a station
/// the journey does not call at and a journey filter that names another journey receive nothing; a
/// quiet stream still sends comment heartbeats; a reconnecting client is caught up in order and
/// exactly once before going live, or told to reset when its position is unknown; a malformed filter
/// is a validation problem; and the response is uncacheable and sets no cookie (design D3, D4, D5,
/// D7).
/// </summary>
/// <remarks>
/// The Host runs with a fixed clock at noon in the railway's time zone, as the neighbouring public
/// tests do, so the seeded timetable and the journey line up whenever the suite runs. The heartbeat
/// is shortened to one second through <c>Distribution:StreamHeartbeatSeconds</c> so the test can
/// observe it without waiting fifteen. Every read has its own timeout, so a stream that never sends
/// fails the test rather than hanging it.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class PublicStreamTests
{
    private const string ControllerSubject = "controller.mahesh";

    /// <summary>The seeded guard account (seed/identity/staff.json), which owns every seeded service.</summary>
    private static readonly Guid GuardStaffId = Guid.Parse("01a15874-e69d-73b2-b24d-d85d22fdead2");

    private static readonly Guid ColomboFort = Guid.Parse("04624d0f-9410-72e3-967f-501ed71f2441");
    private static readonly Guid Kollupitiya = Guid.Parse("dc1ed19b-f522-70a9-ae61-15e47102a42f");
    private static readonly Guid MountLavinia = Guid.Parse("c078a9aa-2f80-7422-a65b-bb7313795bd5");

    /// <summary>A seeded station the test journey never calls at, so its filter matches nothing.</summary>
    private static readonly Guid Bambalapitiya = Guid.Parse("6f818201-ebf9-7374-bbad-74adbab9df38");

    /// <summary>The seeded Coastal Line (seed/network/network.json), which serves the stations above.</summary>
    private static readonly Guid CoastalLine = Guid.Parse("f8493f1c-67b5-76e1-acb6-215d4d9f60ae");

    private static readonly Guid KollupitiyaPlatform = Guid.Parse("e86062e1-aa04-7673-b2dc-51892f077a58");
    private static readonly Guid MountLaviniaPlatform = Guid.Parse("579f8b1e-e3fe-789b-9ba3-20aae4342df3");
    private static readonly Guid ColomboFortPlatformTwo = Guid.Parse("61e67fba-522e-73bf-beab-5cb90e861c86");

    private const string StreamRoute = $"{ApiRoutes.BasePath}/public/stream";

    /// <summary>How long one event may take to reach a connected client.</summary>
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(20);

    /// <summary>How long a quiet stream is watched for stray events and for its heartbeat.</summary>
    private static readonly TimeSpan QuietWindow = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private readonly PostgresFixture _postgres;

    public PublicStreamTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task A_published_delay_reaches_the_matching_streams_once_and_no_other()
    {
        var today = RailwayToday();
        var now = SafeNow(today);
        using var factory = NewFactory(now);
        var journeyId = await CreateJourneyAsync(factory, today, now);
        var auditBaseline = await MaxAuditPositionAsync();

        try
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = false,
            });

            await AssertPublishedDelayReachesTheStreamsAsync(client, journeyId, factory.Logs);
        }
        finally
        {
            await CleanUpAsync(journeyId, auditBaseline);
        }
    }

    [Fact]
    public async Task A_malformed_station_or_journey_filter_is_a_validation_problem()
    {
        using var factory = NewFactory(SafeNow(RailwayToday()));
        using var client = factory.CreateApiClient();
        await StartAsync(factory);

        using var station = await client.GetAsync($"{StreamRoute}?station=not-a-station");
        using var journey = await client.GetAsync($"{StreamRoute}?journey={Guid.Empty}");

        foreach (var response in new[] { station, journey })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        }
    }

    /// <summary>
    /// A client that reconnects with the last position it saw is first sent everything it missed, in
    /// stream-position order and exactly once, and then continues live (live-event-streams spec,
    /// "Resume from last event"; design D4). The two missed changes are committed while no client is
    /// connected, so only the catch-up read can deliver them.
    /// </summary>
    [Fact]
    public async Task A_reconnecting_stream_receives_the_events_it_missed_in_order_once_then_goes_live()
    {
        var today = RailwayToday();
        var now = SafeNow(today);
        using var factory = NewFactory(now);
        var journeyId = await CreateJourneyAsync(factory, today, now);
        var auditBaseline = await MaxAuditPositionAsync();

        try
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = false,
            });

            // The change the client has already received. Its position is a real one, so the resume
            // is a catch-up rather than a reset.
            using var seen = await PostDelayAsync(client, journeyId, fromStopSequence: 2, minutes: 5);
            Assert.Equal(HttpStatusCode.Created, seen.StatusCode);

            var lastSeen = Assert.Single(await StreamPositionsAsync(journeyId));

            // Two changes committed while nobody is connected.
            using var first = await PostDelayAsync(client, journeyId, fromStopSequence: 2, minutes: 10);
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
            using var second = await PostDelayAsync(client, journeyId, fromStopSequence: 3, minutes: 15);
            Assert.Equal(HttpStatusCode.Created, second.StatusCode);

            var positions = await StreamPositionsAsync(journeyId);
            Assert.Equal(3, positions.Count);

            using var stream = await EventStream.OpenAsync(
                client,
                $"?journey={journeyId}",
                lastEventId: lastSeen.ToString(CultureInfo.InvariantCulture));

            var caughtUp = await stream.ReadAsync(EventTimeout, stopAtFirstEvent: false, stopAfterEvents: 2);
            Assert.True(
                caughtUp.Events.Count == 2,
                $"the resumed stream received {caughtUp.Events.Count} catch-up events and "
                + $"{caughtUp.Comments} comments in {EventTimeout}; "
                + $"outbox: {await DeliveryStateAsync(journeyId)}; bus log: {BusLog(factory.Logs)}");

            // Both missed events, in position order, with the IDs the stream sent.
            Assert.Equal(
                positions.Skip(1).Select(position => position.ToString(CultureInfo.InvariantCulture)),
                caughtUp.Events.Select(frame => frame.Id));
            Assert.All(caughtUp.Events, frame => Assert.Equal("delay_published", frame.EventType));

            using (var payload = JsonDocument.Parse(caughtUp.Events[0].Data))
            {
                Assert.Equal(journeyId, payload.RootElement.GetProperty("journeyId").GetGuid());
            }

            // No duplicate follows the join: the bus may still deliver the caught-up changes, and
            // they are skipped by event ID, so the next change is the first event after the catch-up.
            using var third = await PostDelayAsync(client, journeyId, fromStopSequence: 3, minutes: 20);
            Assert.Equal(HttpStatusCode.Created, third.StatusCode);

            var livePositions = await StreamPositionsAsync(journeyId);
            var live = await stream.ReadAsync(EventTimeout, stopAtFirstEvent: true);
            Assert.True(
                live.Events.Count == 1,
                $"the resumed stream received {live.Events.Count} events after the catch-up in "
                + $"{EventTimeout}; outbox: {await DeliveryStateAsync(journeyId)}; "
                + $"bus log: {BusLog(factory.Logs)}");

            // The first event after the catch-up is the new change itself: a late delivery of a
            // caught-up event would have been skipped, not sent ahead of it.
            var liveEvent = Assert.Single(live.Events);
            Assert.Equal(livePositions[^1].ToString(CultureInfo.InvariantCulture), liveEvent.Id);
            Assert.Equal("delay_published", liveEvent.EventType);

            // Nothing follows the live event either.
            var quiet = await stream.ReadAsync(QuietWindow, stopAtFirstEvent: false);
            Assert.Empty(quiet.Events);
        }
        finally
        {
            await CleanUpAsync(journeyId, auditBaseline);
        }
    }

    /// <summary>
    /// A position that cannot be resumed from starts the stream with a <c>reset</c> event telling the
    /// client to reload from the read endpoints, and then continues live (live-event-streams spec,
    /// "Catch-up beyond the retained window"; design D5). A future position and a value that is not a
    /// position are both unknown.
    /// </summary>
    [Fact]
    public async Task A_stream_whose_position_is_unknown_starts_with_reset_then_continues_live()
    {
        var today = RailwayToday();
        var now = SafeNow(today);
        using var factory = NewFactory(now);
        var journeyId = await CreateJourneyAsync(factory, today, now);
        var auditBaseline = await MaxAuditPositionAsync();

        try
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = false,
            });

            using var future = await EventStream.OpenAsync(
                client,
                $"?journey={journeyId}",
                lastEventId: long.MaxValue.ToString(CultureInfo.InvariantCulture));

            var reset = Assert.Single((await future.ReadAsync(EventTimeout, stopAtFirstEvent: true)).Events);
            Assert.Equal("reset", reset.EventType);
            Assert.Null(reset.Id);

            using var malformed = await EventStream.OpenAsync(
                client,
                $"?journey={journeyId}",
                lastEventId: "not-a-position");

            var malformedReset = Assert.Single(
                (await malformed.ReadAsync(EventTimeout, stopAtFirstEvent: true)).Events);
            Assert.Equal("reset", malformedReset.EventType);

            // After the reset the connection is live: the next change arrives with its position.
            using var published = await PostDelayAsync(client, journeyId, fromStopSequence: 2, minutes: 12);
            Assert.Equal(HttpStatusCode.Created, published.StatusCode);

            var position = await StreamPositionAsync(journeyId);
            var live = Assert.Single((await future.ReadAsync(EventTimeout, stopAtFirstEvent: true)).Events);
            Assert.Equal(position.ToString(CultureInfo.InvariantCulture), live.Id);
            Assert.Equal("delay_published", live.EventType);
        }
        finally
        {
            await CleanUpAsync(journeyId, auditBaseline);
        }
    }

    /// <summary>
    /// Opens the three subscriptions, publishes one delay, and reads what each connection receives.
    /// The streams are disposed when this returns, before the test's rows are removed.
    /// </summary>
    private async Task AssertPublishedDelayReachesTheStreamsAsync(
        HttpClient client,
        Guid journeyId,
        LogCapture logs)
    {
        // Registering all three before the change is committed is what makes "received" and
        // "not received" meaningful: every connection existed when the event was offered.
        using var stationStream = await EventStream.OpenAsync(client, $"?station={Kollupitiya}");
        using var journeyStream = await EventStream.OpenAsync(client, $"?journey={journeyId}");
        using var unmatchedStream = await EventStream.OpenAsync(client, $"?station={Bambalapitiya}");

        using var published = await PostDelayAsync(client, journeyId, fromStopSequence: 2, minutes: 15);
        Assert.Equal(HttpStatusCode.Created, published.StatusCode);

        var position = await StreamPositionAsync(journeyId);

        var stationRead = await stationStream.ReadAsync(EventTimeout, stopAtFirstEvent: true);
        Assert.True(
            stationRead.Events.Count == 1,
            $"the matching station stream received {stationRead.Events.Count} events and "
            + $"{stationRead.Comments} comments in {EventTimeout}; "
            + $"outbox: {await DeliveryStateAsync(journeyId)}; bus log: {BusLog(logs)}");

        var stationEvent = stationRead.Events[0];
        Assert.Equal(position.ToString(CultureInfo.InvariantCulture), stationEvent.Id);
        Assert.Equal("delay_published", stationEvent.EventType);

        // The payload is the same live view the detail returns, produced by the same builder and
        // serialiser (design D1).
        using var detail = JsonDocument.Parse(await GetDetailAsync(client, journeyId));
        Assert.Equal(detail.RootElement.GetProperty("data").GetRawText(), stationEvent.Data);

        // The journey filter carries the same event to a subscriber that named that journey.
        var journeyRead = await journeyStream.ReadAsync(EventTimeout, stopAtFirstEvent: true);
        Assert.True(
            journeyRead.Events.Count == 1,
            $"the journey stream received {journeyRead.Events.Count} events in {EventTimeout}; "
            + $"outbox: {await DeliveryStateAsync(journeyId)}");

        var journeyEvent = journeyRead.Events[0];
        Assert.Equal(stationEvent.Id, journeyEvent.Id);
        Assert.Equal(stationEvent.Data, journeyEvent.Data);

        // Nothing is delivered a second time, and the station the journey does not call at never
        // hears about it.
        var stationQuiet = await stationStream.ReadAsync(QuietWindow, stopAtFirstEvent: false);
        Assert.Empty(stationQuiet.Events);
        Assert.True(stationQuiet.Comments > 0, "a quiet matching stream must still send heartbeats.");

        var unmatched = await unmatchedStream.ReadAsync(QuietWindow, stopAtFirstEvent: false);
        Assert.Empty(unmatched.Events);
        Assert.True(unmatched.Comments > 0, "a quiet unmatched stream must still send heartbeats.");
    }

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
        DateTimeOffset now)
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

    private static async Task<string> GetDetailAsync(HttpClient client, Guid journeyId)
    {
        using var response = await client.GetAsync($"{ApiRoutes.BasePath}/public/journeys/{journeyId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadAsStringAsync();
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

        var orphans = await _postgres.QueryAsync(
            $"""
            SELECT count(*)
            FROM operations.outbox_deliveries d
            WHERE d.status = 'pending'
              AND NOT EXISTS (SELECT 1 FROM operations.outbox_messages m WHERE m.id = d.message_id)
            """,
            reader => reader.GetInt64(0));

        return $"{string.Join(" | ", rows)}; orphaned pending deliveries: {string.Join(",", orphans)}";
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
        /// Opens the stream and checks the conventions every public response shares (design D7). A
        /// resume sends the standard <c>Last-Event-ID</c> header, which is the only way a position is
        /// passed: there is no query parameter for it.
        /// </summary>
        public static async Task<EventStream> OpenAsync(
            HttpClient client,
            string query,
            string? lastEventId = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, StreamRoute + query);

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
