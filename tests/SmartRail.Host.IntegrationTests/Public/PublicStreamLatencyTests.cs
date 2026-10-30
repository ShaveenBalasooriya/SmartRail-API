using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
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
/// How quickly a committed change reaches an already-open stream (live-event-streams spec, "Public
/// event stream" and "Guard stream scoped to own journeys"). A connection opened before the publish
/// must receive the frame from the change itself, not from the heartbeat that follows it: the
/// heartbeat is deliberately set to the longest the Host allows, twenty seconds, which is longer
/// than the ten-second read window, so a frame that only arrives with a heartbeat fails the read
/// instead of passing quietly. The public stream and the Guard stream are checked from the same
/// publish, because both are fed by the same consumer.
/// </summary>
/// <remarks>
/// The neighbouring stream tests shorten the heartbeat to one second, which cannot tell a frame
/// pushed by the change from a frame written at the next heartbeat. This test exists to make that
/// distinction, so it runs with a heartbeat far longer than the latency it asserts. The Host's clock
/// is frozen at noon in the railway's time zone, as the neighbouring public tests do.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class PublicStreamLatencyTests
{
    private const string ControllerSubject = "controller.mahesh";
    private const string GuardSubject = "guard.ishara";

    /// <summary>The seeded guard account (seed/identity/staff.json), which owns the journey below.</summary>
    private static readonly Guid GuardStaffId = Guid.Parse("01a15874-e69d-73b2-b24d-d85d22fdead2");

    private static readonly Guid ColomboFort = Guid.Parse("04624d0f-9410-72e3-967f-501ed71f2441");
    private static readonly Guid Kollupitiya = Guid.Parse("dc1ed19b-f522-70a9-ae61-15e47102a42f");
    private static readonly Guid MountLavinia = Guid.Parse("c078a9aa-2f80-7422-a65b-bb7313795bd5");

    /// <summary>The seeded Coastal Line (seed/network/network.json), which serves the stations above.</summary>
    private static readonly Guid CoastalLine = Guid.Parse("f8493f1c-67b5-76e1-acb6-215d4d9f60ae");

    private static readonly Guid KollupitiyaPlatform = Guid.Parse("e86062e1-aa04-7673-b2dc-51892f077a58");
    private static readonly Guid MountLaviniaPlatform = Guid.Parse("579f8b1e-e3fe-789b-9ba3-20aae4342df3");
    private static readonly Guid ColomboFortPlatformTwo = Guid.Parse("61e67fba-522e-73bf-beab-5cb90e861c86");

    private static readonly string PublicStreamRoute = $"{ApiRoutes.BasePath}/public/stream";
    private static readonly string GuardStreamRoute = $"{ApiRoutes.BasePath}/guard/stream";

    /// <summary>How long a change may take to reach a connection that was already open.</summary>
    private static readonly TimeSpan LatencyBudget = TimeSpan.FromSeconds(3);

    /// <summary>
    /// How long a read waits before it declares the frame missing. Longer than the budget, shorter
    /// than the twenty-second heartbeat, so a heartbeat can never supply the frame.
    /// </summary>
    private static readonly TimeSpan ReadWindow = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private readonly PostgresFixture _postgres;

    public PublicStreamLatencyTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task A_published_delay_reaches_an_open_public_stream_and_an_open_guard_stream_promptly()
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

            // Both connections exist before the change is committed, so the frame each one reads is
            // the change's own delivery rather than a catch-up for a connection opened afterwards.
            using var publicStream = await EventStream.OpenAsync(client, PublicStreamRoute);
            using var guardStream = await EventStream.OpenAsync(
                client,
                GuardStreamRoute,
                TestTokens.Create(GuardSubject, Roles.Guard));

            // Both reads wait on the same publish, so neither connection is read late because the
            // other was read first.
            var stopwatch = Stopwatch.StartNew();
            using var published = await PostDelayAsync(client, journeyId, fromStopSequence: 2, minutes: 15);
            Assert.Equal(HttpStatusCode.Created, published.StatusCode);

            var reads = await Task.WhenAll(
                publicStream.ReadAsync(ReadWindow, stopAtFirstEvent: true),
                guardStream.ReadAsync(ReadWindow, stopAtFirstEvent: true));

            stopwatch.Stop();

            var publicRead = reads[0];
            Assert.True(
                publicRead.Events.Count == 1,
                $"the public stream should receive the published delay within {ReadWindow}; it received "
                + $"{publicRead.Events.Count} events and {publicRead.Comments} comments; "
                + $"outbox: {await DeliveryStateAsync(journeyId)}; bus log: {BusLog(factory.Logs)}");

            var guardRead = reads[1];
            Assert.True(
                guardRead.Events.Count == 1,
                $"the Guard's stream should receive the published delay within {ReadWindow}; it received "
                + $"{guardRead.Events.Count} events and {guardRead.Comments} comments; "
                + $"outbox: {await DeliveryStateAsync(journeyId)}; bus log: {BusLog(factory.Logs)}");

            Assert.Equal("delay_published", publicRead.Events[0].EventType);
            Assert.Equal("delay_published", guardRead.Events[0].EventType);

            Assert.True(
                stopwatch.Elapsed < LatencyBudget,
                $"the published delay should reach both open streams within "
                + $"{LatencyBudget.TotalSeconds} seconds, but the first frames arrived after "
                + $"{stopwatch.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)} seconds; "
                + $"outbox: {await DeliveryStateAsync(journeyId)}; bus log: {BusLog(factory.Logs)}");
        }
        finally
        {
            await CleanUpAsync(journeyId, auditBaseline);
        }
    }

    /// <summary>
    /// The longest heartbeat the Host allows, twenty seconds, which is longer than the read window,
    /// so a frame written at the next heartbeat cannot arrive inside it.
    /// </summary>
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
                ["Distribution:StreamHeartbeatSeconds"] = "20",
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

    /// <summary>
    /// The delivery state of the journey's announcements, so a stream that received nothing says
    /// whether the bus ever offered it and on which attempt.
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

        /// <summary>Opens the route and checks the conventions every stream response shares.</summary>
        public static async Task<EventStream> OpenAsync(HttpClient client, string route, string? token = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, route);

            if (token is not null)
            {
                request.WithStaffToken(token);
            }

            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());

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
