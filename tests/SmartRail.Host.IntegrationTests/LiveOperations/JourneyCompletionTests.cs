using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartRail.Host.Http;
using SmartRail.Host.IntegrationTests.Infrastructure;
using SmartRail.LiveOperations.Application.LiveState.CompleteFinishedJourneys;
using SmartRail.RailNetwork.Contracts;
using SmartRail.RailNetwork.Infrastructure.Persistence;
using SmartRail.SharedKernel.Application.Abstractions;
using SmartRail.SharedKernel.Application.Events;

namespace SmartRail.Host.IntegrationTests.LiveOperations;

/// <summary>
/// The journeys the periodic check completes (departure-logging spec, "Journeys complete
/// automatically"; design D5): a journey whose penultimate calling stop has departed becomes
/// <c>completed</c> once its expected arrival at the terminus passes, with one
/// <c>journey_completed</c> event and its announcement, and a second tick appends nothing. A
/// terminated or cancelled journey is never touched, and a running journey whose penultimate stop
/// was never logged stays running.
/// </summary>
/// <remarks>
/// The Host runs with a clock the test can move forward, so "the expected arrival has passed" is
/// deterministic whatever the wall clock says, and the check runs on demand or on one real tick of
/// the hosted service with a one-second interval.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class JourneyCompletionTests
{
    /// <summary>The seeded guard account (seed/identity/staff.json).</summary>
    private static readonly Guid GuardStaffId = Guid.Parse("01a15874-e69d-73b2-b24d-d85d22fdead2");

    private const string GuardSubject = "guard.ishara";

    private const string ControllerSubject = "controller.mahesh";

    private static readonly string DepartureLogsRoute = $"{ApiRoutes.BasePath}/guard/departure-logs";

    private readonly PostgresFixture _postgres;

    public JourneyCompletionTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task The_final_leg_arrives_and_the_journey_completes_once_with_one_announcement()
    {
        var today = RailwayToday();
        var now = SafeNow(today);
        var clock = new MutableTimeProvider(now);
        using var factory = NewFactory(clock);
        var stops = FinalLegStops(now);
        var journeyId = await CreateJourneyAsync(factory, today, stops);
        var auditBaseline = await MaxAuditPositionAsync();

        try
        {
            using var client = factory.CreateApiClient();

            // The guard logs the penultimate calling stop, which starts the journey.
            using var logged = await PostDepartureAsync(client, journeyId, 1, now.AddMinutes(-28));

            Assert.Equal(HttpStatusCode.Created, logged.StatusCode);
            Assert.Equal("running", await StatusAsync(journeyId));
            Assert.Equal(["departure_logged"], await EventTypesAsync(journeyId));

            // The expected arrival at the terminus passes: the check is the only thing that acts.
            clock.Advance(TimeSpan.FromMinutes(20));

            Assert.Equal(1, await SendCompletionAsync(factory));

            Assert.Equal("completed", await StatusAsync(journeyId));

            var events = await EventsAsync(journeyId);
            Assert.Equal(["departure_logged", "journey_completed"], events.Select(row => row.EventType));

            var completion = events[1];
            Assert.Equal(2, completion.SequenceNumber);
            Assert.Null(completion.DepartureLogId);
            Assert.Null(completion.StatusChangeId);
            Assert.Equal(clock.GetUtcNow(), completion.OccurredAt);
            Assert.Equal(clock.GetUtcNow(), completion.RecordedAt);

            using (var payload = JsonDocument.Parse(completion.Payload))
            {
                Assert.Equal("completed", payload.RootElement.GetProperty("status").GetString());
            }

            // One announcement per journey event, and this one is delivered to the bus only after
            // the transaction committed.
            Assert.Equal(2, await OutboxCountAsync(journeyId));
            Assert.Equal(completion.Id, Assert.Single(await _postgres.QueryAsync(
                $"SELECT id FROM operations.outbox_messages WHERE id = '{completion.Id}'",
                reader => reader.GetGuid(0))));
            await WaitForPublishedAsync(completion.Id);

            // The same journey reaching both views the operations staff read.
            Assert.Equal("completed", await ControllerStatusAsync(client, journeyId));
            Assert.Equal("completed", await GuardStatusAsync(client, journeyId));

            // The check runs again: the journey is no longer running, so nothing is appended.
            Assert.Equal(0, await SendCompletionAsync(factory));

            Assert.Equal(["departure_logged", "journey_completed"], await EventTypesAsync(journeyId));
            Assert.Equal(2, await OutboxCountAsync(journeyId));
        }
        finally
        {
            await CleanUpJourneyAsync(journeyId);
            await ResetAuditEntriesAsync(auditBaseline);
        }
    }

    [Fact]
    public async Task A_journey_ended_early_stays_terminated_and_is_never_completed()
    {
        var today = RailwayToday();
        var now = SafeNow(today);
        var clock = new MutableTimeProvider(now);
        using var factory = NewFactory(clock);
        var journeyId = await CreateJourneyAsync(factory, today, FourCallingStops(now));
        var auditBaseline = await MaxAuditPositionAsync();

        try
        {
            using var client = factory.CreateApiClient();

            using var logged = await PostDepartureAsync(client, journeyId, 1, now.AddMinutes(-28));
            Assert.Equal(HttpStatusCode.Created, logged.StatusCode);

            using var termination = await PublishTerminationAsync(client, journeyId, lastStopSequence: 2);
            Assert.Equal(HttpStatusCode.Created, termination.StatusCode);
            Assert.Equal("terminated", await StatusAsync(journeyId));

            // Everything the timetable expected at the terminus is long past.
            clock.Advance(TimeSpan.FromMinutes(40));

            Assert.Equal(0, await SendCompletionAsync(factory));

            Assert.Equal("terminated", await StatusAsync(journeyId));
            Assert.DoesNotContain("journey_completed", await EventTypesAsync(journeyId));
        }
        finally
        {
            await CleanUpJourneyAsync(journeyId);
            await ResetAuditEntriesAsync(auditBaseline);
        }
    }

    [Fact]
    public async Task A_running_journey_whose_penultimate_stop_was_never_logged_stays_running()
    {
        var today = RailwayToday();
        var now = SafeNow(today);
        var clock = new MutableTimeProvider(now);
        using var factory = NewFactory(clock);
        var journeyId = await CreateJourneyAsync(factory, today, ThreeCallingStops(now));
        var auditBaseline = await MaxAuditPositionAsync();

        try
        {
            using var client = factory.CreateApiClient();

            // Only the first stop is logged; the penultimate calling stop never departs.
            using var logged = await PostDepartureAsync(client, journeyId, 1, now.AddMinutes(-28));
            Assert.Equal(HttpStatusCode.Created, logged.StatusCode);

            clock.Advance(TimeSpan.FromMinutes(40));

            // The journey is loaded by the check, but the calculator leaves it running: the Guard's
            // log is the only source of progress.
            Assert.Equal(1, await SendCompletionAsync(factory));

            Assert.Equal("running", await StatusAsync(journeyId));
            Assert.Equal(["departure_logged"], await EventTypesAsync(journeyId));
        }
        finally
        {
            await CleanUpJourneyAsync(journeyId);
            await ResetAuditEntriesAsync(auditBaseline);
        }
    }

    [Fact]
    public async Task A_cancelled_journey_is_untouched()
    {
        var today = RailwayToday();
        var now = SafeNow(today);
        var clock = new MutableTimeProvider(now);
        using var factory = NewFactory(clock);
        var journeyId = await CreateJourneyAsync(factory, today, FinalLegStops(now));
        var auditBaseline = await MaxAuditPositionAsync();

        try
        {
            using var client = factory.CreateApiClient();

            using var cancellation = await PublishCancellationAsync(client, journeyId);
            Assert.Equal(HttpStatusCode.Created, cancellation.StatusCode);
            Assert.Equal("cancelled", await StatusAsync(journeyId));

            clock.Advance(TimeSpan.FromMinutes(40));

            Assert.Equal(0, await SendCompletionAsync(factory));

            Assert.Equal("cancelled", await StatusAsync(journeyId));
            Assert.Equal(["cancellation_published"], await EventTypesAsync(journeyId));
        }
        finally
        {
            await CleanUpJourneyAsync(journeyId);
            await ResetAuditEntriesAsync(auditBaseline);
        }
    }

    [Fact]
    public async Task One_tick_of_the_hosted_service_completes_the_journey()
    {
        var today = RailwayToday();
        var now = SafeNow(today);
        var clock = new MutableTimeProvider(now);
        using var factory = NewFactory(clock, completionCheckSeconds: 1);
        var journeyId = await CreateJourneyAsync(factory, today, FinalLegStops(now));
        var auditBaseline = await MaxAuditPositionAsync();

        try
        {
            using var client = factory.CreateApiClient();

            using var logged = await PostDepartureAsync(client, journeyId, 1, now.AddMinutes(-28));
            Assert.Equal(HttpStatusCode.Created, logged.StatusCode);

            // The hosted service fires only after its interval, so the first tick is what completes
            // the journey once the terminus arrival has passed.
            clock.Advance(TimeSpan.FromMinutes(20));

            var deadline = DateTimeOffset.UtcNow.AddSeconds(20);

            while (await StatusAsync(journeyId) != "completed")
            {
                Assert.True(
                    DateTimeOffset.UtcNow < deadline,
                    "The hosted completion check did not complete the journey within 20 seconds.");

                await Task.Delay(TimeSpan.FromMilliseconds(100));
            }

            Assert.Equal(["departure_logged", "journey_completed"], await EventTypesAsync(journeyId));
        }
        finally
        {
            await CleanUpJourneyAsync(journeyId);
            await ResetAuditEntriesAsync(auditBaseline);
        }
    }

    private SmartRailApiFactory NewFactory(MutableTimeProvider clock, int? completionCheckSeconds = null) =>
        new(
            _postgres.ConnectionString,
            configureServices: services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(clock);
            },
            settings: completionCheckSeconds is { } seconds
                ? new Dictionary<string, string>
                {
                    ["Operations:CompletionCheckSeconds"] = seconds.ToString(CultureInfo.InvariantCulture),
                }
                : null);

    private static DateOnly RailwayToday() => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTime(
            DateTimeOffset.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Colombo")).DateTime);

    /// <summary>
    /// Noon in the railway's time zone on the given day, as UTC: safely inside the day, so the
    /// two-minute-future bound and the service-date window are the same whenever the test runs.
    /// </summary>
    private static DateTimeOffset SafeNow(DateOnly serviceDate) =>
        new DateTimeOffset(
            serviceDate.ToDateTime(new TimeOnly(12, 0)),
            TimeSpan.FromHours(5.5)).ToUniversalTime();

    /// <summary>
    /// Two calling stops: the first departs half an hour before the clock and the terminus is
    /// expected ten minutes after it, so the penultimate stop can be logged now and the arrival
    /// passes when the test advances the clock.
    /// </summary>
    private static List<JourneyStopDto> FinalLegStops(DateTimeOffset now) =>
    [
        new(1, Guid.CreateVersion7(), true, null, now.AddMinutes(-30), Guid.CreateVersion7()),
        new(2, Guid.CreateVersion7(), true, now.AddMinutes(10), null, null),
    ];

    /// <summary>Three calling stops; the penultimate one is stop 2.</summary>
    private static List<JourneyStopDto> ThreeCallingStops(DateTimeOffset now)
    {
        var departure = now.AddMinutes(-30);

        return
        [
            new(1, Guid.CreateVersion7(), true, null, departure, Guid.CreateVersion7()),
            new(2, Guid.CreateVersion7(), true, departure.AddMinutes(10), departure.AddMinutes(11), Guid.CreateVersion7()),
            new(3, Guid.CreateVersion7(), true, departure.AddMinutes(20), null, null),
        ];
    }

    /// <summary>Four calling stops, so a journey can be ended early at stop 2.</summary>
    private static List<JourneyStopDto> FourCallingStops(DateTimeOffset now)
    {
        var departure = now.AddMinutes(-30);

        return
        [
            new(1, Guid.CreateVersion7(), true, null, departure, Guid.CreateVersion7()),
            new(2, Guid.CreateVersion7(), true, departure.AddMinutes(10), departure.AddMinutes(11), Guid.CreateVersion7()),
            new(3, Guid.CreateVersion7(), true, departure.AddMinutes(20), departure.AddMinutes(21), Guid.CreateVersion7()),
            new(4, Guid.CreateVersion7(), true, departure.AddMinutes(30), null, null),
        ];
    }

    private static async Task StartAsync(SmartRailApiFactory factory)
    {
        using var client = factory.CreateApiClient();
        using var response = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>Creates the journey's live state through the real generator announcement.</summary>
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
                Guid.CreateVersion7(),
                ServiceDirection.Outbound,
                GuardStaffId,
                stops),
            journeyId);

        await database.SaveChangesAsync();

        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
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

    private static async Task<int> SendCompletionAsync(SmartRailApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var result = await scope.ServiceProvider
            .GetRequiredService<ISender>()
            .Send(new CompleteFinishedJourneysCommand());

        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static async Task<HttpResponseMessage> PostDepartureAsync(
        HttpClient client,
        Guid journeyId,
        int stopSequence,
        DateTimeOffset departedAt)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, DepartureLogsRoute)
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

    private static async Task<HttpResponseMessage> PublishCancellationAsync(
        HttpClient client,
        Guid journeyId)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{ApiRoutes.BasePath}/operations/journeys/{journeyId}/cancellation");
        request.WithStaffToken(TestTokens.Create(ControllerSubject, Roles.NetworkController));

        return await client.SendAsync(request);
    }

    /// <summary>The lifecycle status the Network Controller's live view reports.</summary>
    private static async Task<string> ControllerStatusAsync(HttpClient client, Guid journeyId)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{ApiRoutes.BasePath}/operations/journeys/{journeyId}");
        request.WithStaffToken(TestTokens.Create(ControllerSubject, Roles.NetworkController));

        return await ReadJourneyStatusAsync(client, request);
    }

    /// <summary>The lifecycle status the Guard's app sees for its own journey.</summary>
    private static async Task<string> GuardStatusAsync(HttpClient client, Guid journeyId)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{ApiRoutes.BasePath}/guard/journeys/{journeyId}");
        request.WithStaffToken(TestTokens.Create(GuardSubject, Roles.Guard));

        return await ReadJourneyStatusAsync(client, request);
    }

    private static async Task<string> ReadJourneyStatusAsync(
        HttpClient client,
        HttpRequestMessage request)
    {
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return body.RootElement.GetProperty("journey").GetProperty("status").GetString()!;
    }

    private async Task<int> CountAsync(string sql)
    {
        var count = Assert.Single(await _postgres.QueryAsync(sql, reader => reader.GetInt64(0)));
        return (int)count;
    }

    private async Task<int> OutboxCountAsync(Guid journeyId) =>
        await CountAsync(
            $"SELECT count(*) FROM operations.outbox_messages WHERE ordering_key = '{journeyId}'");

    private async Task<string> StatusAsync(Guid journeyId)
    {
        var statuses = await _postgres.QueryAsync(
            $"SELECT lifecycle_status FROM operations.journey_live_states WHERE journey_id = '{journeyId}'",
            reader => reader.GetString(0));

        return Assert.Single(statuses);
    }

    private async Task<List<EventRow>> EventsAsync(Guid journeyId) =>
        await _postgres.QueryAsync(
            $"""
            SELECT id, sequence_number, event_type, departure_log_id, status_change_id,
                   occurred_at, recorded_at, payload::text
            FROM operations.journey_events
            WHERE journey_id = '{journeyId}'
            ORDER BY sequence_number
            """,
            reader => new EventRow(
                reader.GetGuid(0),
                reader.GetInt64(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetGuid(3),
                reader.IsDBNull(4) ? null : reader.GetGuid(4),
                reader.GetFieldValue<DateTimeOffset>(5),
                reader.GetFieldValue<DateTimeOffset>(6),
                reader.GetString(7)));

    private async Task<List<string>> EventTypesAsync(Guid journeyId) =>
        (await EventsAsync(journeyId)).Select(row => row.EventType).ToList();

    /// <summary>Waits for the dispatcher to accept the announcement, which it does only after commit.</summary>
    private async Task WaitForPublishedAsync(Guid messageId)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);

        while (true)
        {
            var published = await CountAsync(
                $"SELECT count(*) FROM operations.outbox_messages WHERE id = '{messageId}' AND published_at IS NOT NULL");

            if (published == 1)
            {
                return;
            }

            Assert.True(
                DateTimeOffset.UtcNow < deadline,
                "The completion announcement was not published to the event bus within 10 seconds.");

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
    }

    private async Task<long> MaxAuditPositionAsync() =>
        Assert.Single(await _postgres.QueryAsync(
            "SELECT coalesce(max(chain_position), 0) FROM audit.entries",
            reader => reader.GetInt64(0)));

    /// <summary>Removes every row the test created for the journey.</summary>
    private async Task CleanUpJourneyAsync(Guid journeyId)
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
    }

    /// <summary>
    /// Removes the audit entries this test appended, leaving the earlier chain intact. The table is
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

    private sealed record EventRow(
        Guid Id,
        long SequenceNumber,
        string EventType,
        Guid? DepartureLogId,
        Guid? StatusChangeId,
        DateTimeOffset OccurredAt,
        DateTimeOffset RecordedAt,
        string Payload);

    /// <summary>A clock the test moves forward, so the passing of the terminus arrival is exact.</summary>
    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private long _utcTicks = now.UtcTicks;

        public void Advance(TimeSpan by) => Interlocked.Add(ref _utcTicks, by.Ticks);

        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _utcTicks), TimeSpan.Zero);
    }
}
