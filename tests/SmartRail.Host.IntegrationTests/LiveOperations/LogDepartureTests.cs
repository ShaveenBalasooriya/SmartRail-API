using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartRail.Audit.Contracts;
using SmartRail.Host.Http;
using SmartRail.Host.IntegrationTests.Infrastructure;
using SmartRail.RailNetwork.Contracts;
using SmartRail.RailNetwork.Infrastructure.Persistence;
using SmartRail.SharedKernel.Application.Abstractions;
using SmartRail.SharedKernel.Application.Events;

namespace SmartRail.Host.IntegrationTests.LiveOperations;

/// <summary>
/// Logging a departure through <c>POST /guard/departure-logs</c> (departure-logging spec, task 2.3):
/// the sync contract the offline Guard app retries blindly, the one departure per stop, the original
/// device time kept apart from the server receipt time, the plausibility and stop rules, the
/// service-date window, the cancelled and terminated journeys that still accept a log, the guard-only
/// and assignment rules with their audited denial, and the shared transaction that leaves nothing
/// behind when the audit step fails.
/// </summary>
/// <remarks>
/// The Host runs with a fixed clock on a safe hour of the current railway day, so a departure's
/// two-minute-future bound and "today or yesterday" are deterministic whatever time the test runs at.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class LogDepartureTests
{
    /// <summary>The seeded guard account (seed/identity/staff.json).</summary>
    private static readonly Guid GuardStaffId = Guid.Parse("01a15874-e69d-73b2-b24d-d85d22fdead2");

    /// <summary>
    /// The seeded auditor account, used as the second guard's staff id: the role comes from the
    /// token's group, the staff id from its subject (the seed holds one guard), so a token for this
    /// subject holding the guard group is a guard with a different staff id.
    /// </summary>
    private static readonly Guid SecondGuardStaffId = Guid.Parse("01a15874-e6a0-7fd3-8c46-34bd36e8f16d");

    private const string GuardSubject = "guard.ishara";

    private const string SecondGuardSubject = "auditor.wasantha";

    private const string ControllerSubject = "controller.mahesh";

    /// <summary>The seeded auditor account, used for the auditor-only-write refusal.</summary>
    private const string AuditorSubject = "auditor.wasantha";

    private static readonly string DepartureLogsRoute = $"{ApiRoutes.BasePath}/guard/departure-logs";

    /// <summary>How long a denial may take to be delivered and recorded before the test fails.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private readonly PostgresFixture _postgres;

    public LogDepartureTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task A_new_log_records_the_departure_event_outbox_row_and_audit_entry_and_a_replay_changes_nothing()
    {
        var today = RailwayToday();
        var now = SafeNow(today);
        using var factory = NewFactory(now);
        var journeyId = await CreateJourneyAsync(factory, today, GuardStaffId, CallingStops(DepartAt(today, 2), 3));
        var auditBaseline = await MaxAuditPositionAsync();
        var logId = Guid.CreateVersion7();
        var departedAt = DepartAt(today, 2).AddMinutes(2);

        try
        {
            using var client = factory.CreateApiClient();
            using var created = await PostDepartureAsync(client, logId, journeyId, 1, departedAt, "crew");

            Assert.Equal(HttpStatusCode.Created, created.StatusCode);

            var body = await ReadLogAsync(created);
            Assert.Equal(logId, body.Id);
            Assert.Equal(journeyId, body.JourneyId);
            Assert.Equal(1, body.StopSequence);
            Assert.Equal(departedAt, body.DepartedAt);
            Assert.Equal("crew", body.DelayCause);
            Assert.Equal(now, body.ReceivedAt);

            // The stop's actual departure is the submitted device time, not the receipt time.
            Assert.Equal(departedAt, await ActualDepartureAsync(journeyId, 1));

            var journeyEvent = Assert.Single(await _postgres.QueryAsync(
                $"""
                SELECT event_type, departure_log_id, occurred_at, recorded_at
                FROM operations.journey_events
                WHERE journey_id = '{journeyId}'
                """,
                reader => new JourneyEventRow(
                    reader.GetString(0),
                    reader.GetGuid(1),
                    reader.GetFieldValue<DateTimeOffset>(2),
                    reader.GetFieldValue<DateTimeOffset>(3))));

            Assert.Equal("departure_logged", journeyEvent.Type);
            Assert.Equal(logId, journeyEvent.DepartureLogId);
            Assert.Equal(departedAt, journeyEvent.OccurredAt);
            Assert.Equal(now, journeyEvent.RecordedAt);

            Assert.Equal(1, await CountAsync(
                $"SELECT count(*) FROM operations.outbox_messages WHERE ordering_key = '{journeyId}'"));

            var entry = await ReadSuccessEntryAsync(journeyId);
            Assert.Equal(GuardStaffId, entry.Actor);
            Assert.Equal(Roles.Guard, entry.Role);
            Assert.Equal("LogDeparture", entry.Action);
            Assert.Equal("journey", entry.TargetType);
            Assert.Equal(journeyId.ToString(), entry.TargetId);
            Assert.Equal("succeeded", entry.Outcome);

            using (var before = JsonDocument.Parse(entry.Before!))
            {
                Assert.Equal(1, before.RootElement.GetProperty("stopSequence").GetInt32());
                Assert.Equal(JsonValueKind.Null, before.RootElement.GetProperty("departedAt").ValueKind);
            }

            using (var after = JsonDocument.Parse(entry.After!))
            {
                Assert.Equal(1, after.RootElement.GetProperty("stopSequence").GetInt32());
                Assert.Equal(departedAt, after.RootElement.GetProperty("departedAt").GetDateTimeOffset());
            }

            // An identical resend is a replay: 200, the same record, and nothing else happens.
            using var replayed = await PostDepartureAsync(client, logId, journeyId, 1, departedAt, "crew");

            Assert.Equal(HttpStatusCode.OK, replayed.StatusCode);
            Assert.Equal(body, await ReadLogAsync(replayed));
            await AssertOnlyOneOfEverythingAsync(journeyId);
            Assert.Equal(1, await CountAsync(
                $"SELECT count(*) FROM audit.entries WHERE action = 'LogDeparture' AND target_id = '{journeyId}'"));

            // The same identifier with different content is a conflict, and changes nothing.
            using var conflict = await PostDepartureAsync(
                client,
                logId,
                journeyId,
                2,
                departedAt.AddMinutes(10),
                "crew");

            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            Assert.Equal("application/problem+json", conflict.Content.Headers.ContentType?.MediaType);
            await AssertOnlyOneOfEverythingAsync(journeyId);
            Assert.Null(await ActualDepartureAsync(journeyId, 2));
        }
        finally
        {
            await CleanUpJourneyAsync(journeyId);
            await ResetAuditEntriesAsync(auditBaseline);
        }
    }

    [Fact]
    public async Task A_double_tap_with_two_identifiers_accepts_the_first_and_conflicts_the_second()
    {
        var today = RailwayToday();
        using var factory = NewFactory(SafeNow(today));
        var journeyId = await CreateJourneyAsync(factory, today, GuardStaffId, CallingStops(DepartAt(today, 2), 3));
        var auditBaseline = await MaxAuditPositionAsync();

        try
        {
            using var client = factory.CreateApiClient();

            using var first = await PostDepartureAsync(
                client,
                Guid.CreateVersion7(),
                journeyId,
                1,
                DepartAt(today, 2).AddMinutes(2));

            using var second = await PostDepartureAsync(
                client,
                Guid.CreateVersion7(),
                journeyId,
                1,
                DepartAt(today, 2).AddMinutes(3));

            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
            await AssertOnlyOneOfEverythingAsync(journeyId);
        }
        finally
        {
            await CleanUpJourneyAsync(journeyId);
            await ResetAuditEntriesAsync(auditBaseline);
        }
    }

    [Fact]
    public async Task Two_identical_requests_at_once_answer_one_created_and_one_replayed()
    {
        var today = RailwayToday();
        using var factory = NewFactory(SafeNow(today));
        var journeyId = await CreateJourneyAsync(factory, today, GuardStaffId, CallingStops(DepartAt(today, 2), 3));
        var auditBaseline = await MaxAuditPositionAsync();
        var logId = Guid.CreateVersion7();
        var departedAt = DepartAt(today, 2).AddMinutes(2);

        try
        {
            using var client = factory.CreateApiClient();

            var responses = await Task.WhenAll(
                PostDepartureAsync(client, logId, journeyId, 1, departedAt),
                PostDepartureAsync(client, logId, journeyId, 1, departedAt));

            try
            {
                // The row lock serialises the two: the second sees the committed log and replays it.
                Assert.Equal(
                    [HttpStatusCode.OK, HttpStatusCode.Created],
                    responses.Select(response => response.StatusCode).OrderBy(status => status).ToArray());
            }
            finally
            {
                foreach (var response in responses)
                {
                    response.Dispose();
                }
            }

            await AssertOnlyOneOfEverythingAsync(journeyId);
        }
        finally
        {
            await CleanUpJourneyAsync(journeyId);
            await ResetAuditEntriesAsync(auditBaseline);
        }
    }

    [Fact]
    public async Task Two_different_identifiers_for_the_same_stop_at_once_answer_one_created_and_one_conflict()
    {
        var today = RailwayToday();
        using var factory = NewFactory(SafeNow(today));
        var journeyId = await CreateJourneyAsync(factory, today, GuardStaffId, CallingStops(DepartAt(today, 2), 3));
        var auditBaseline = await MaxAuditPositionAsync();
        var departedAt = DepartAt(today, 2).AddMinutes(2);

        try
        {
            using var client = factory.CreateApiClient();

            var responses = await Task.WhenAll(
                PostDepartureAsync(client, Guid.CreateVersion7(), journeyId, 1, departedAt),
                PostDepartureAsync(client, Guid.CreateVersion7(), journeyId, 1, departedAt));

            try
            {
                // The unique (journey_id, stop_sequence) index settles the tap race on the second.
                Assert.Equal(
                    [HttpStatusCode.Created, HttpStatusCode.Conflict],
                    responses.Select(response => response.StatusCode).OrderBy(status => status).ToArray());
            }
            finally
            {
                foreach (var response in responses)
                {
                    response.Dispose();
                }
            }

            await AssertOnlyOneOfEverythingAsync(journeyId);
        }
        finally
        {
            await CleanUpJourneyAsync(journeyId);
            await ResetAuditEntriesAsync(auditBaseline);
        }
    }

    [Fact]
    public async Task Offline_logs_keep_their_device_times_and_record_the_server_receipt_time()
    {
        var today = RailwayToday();
        var now = SafeNow(today);
        using var factory = NewFactory(now);
        var journeyId = await CreateJourneyAsync(factory, today, GuardStaffId, CallingStops(DepartAt(today, 2), 3));
        var auditBaseline = await MaxAuditPositionAsync();
        var firstTap = now.AddMinutes(-23);
        var secondTap = now.AddMinutes(-16);

        try
        {
            using var client = factory.CreateApiClient();

            using var first = await PostDepartureAsync(
                client,
                Guid.CreateVersion7(),
                journeyId,
                1,
                firstTap);

            using var second = await PostDepartureAsync(
                client,
                Guid.CreateVersion7(),
                journeyId,
                2,
                secondTap);

            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
            Assert.Equal(HttpStatusCode.Created, second.StatusCode);

            var firstBody = await ReadLogAsync(first);
            var secondBody = await ReadLogAsync(second);

            Assert.Equal(firstTap, firstBody.DepartedAt);
            Assert.Equal(secondTap, secondBody.DepartedAt);
            Assert.Equal(now, firstBody.ReceivedAt);
            Assert.Equal(now, secondBody.ReceivedAt);

            var rows = await _postgres.QueryAsync(
                $"""
                SELECT stop_sequence, departed_at, received_at
                FROM operations.departure_logs
                WHERE journey_id = '{journeyId}'
                ORDER BY stop_sequence
                """,
                reader => (
                    StopSequence: reader.GetInt32(0),
                    DepartedAt: reader.GetFieldValue<DateTimeOffset>(1),
                    ReceivedAt: reader.GetFieldValue<DateTimeOffset>(2)));

            Assert.Equal([1, 2], rows.Select(row => row.StopSequence).ToArray());
            Assert.Equal(firstTap, rows[0].DepartedAt);
            Assert.Equal(secondTap, rows[1].DepartedAt);
            Assert.All(rows, row => Assert.Equal(now, row.ReceivedAt));
            Assert.Equal(firstTap, await ActualDepartureAsync(journeyId, 1));
            Assert.Equal(secondTap, await ActualDepartureAsync(journeyId, 2));
        }
        finally
        {
            await CleanUpJourneyAsync(journeyId);
            await ResetAuditEntriesAsync(auditBaseline);
        }
    }

    [Fact]
    public async Task A_stop_the_service_does_not_call_at_is_a_bad_request_naming_the_stop()
    {
        var today = RailwayToday();
        using var factory = NewFactory(SafeNow(today));
        var departure = DepartAt(today, 2);
        var journeyId = await CreateJourneyAsync(
            factory,
            today,
            GuardStaffId,
            [
                new JourneyStopDto(1, Guid.CreateVersion7(), true, null, departure, Guid.CreateVersion7()),
                new JourneyStopDto(2, Guid.CreateVersion7(), false, null, null, null),
                new JourneyStopDto(3, Guid.CreateVersion7(), true, departure.AddMinutes(20), null, null),
            ]);

        try
        {
            using var client = factory.CreateApiClient();
            using var response = await PostDepartureAsync(
                client,
                Guid.CreateVersion7(),
                journeyId,
                2,
                departure.AddMinutes(2));

            await AssertBadRequestAsync(response, "DepartureLog.StopNotCalling");
            Assert.Contains("Stop 2", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            Assert.Equal(0, await DepartureLogCountAsync(journeyId));
        }
        finally
        {
            await CleanUpJourneyAsync(journeyId);
        }
    }

    [Fact]
    public async Task The_final_stop_is_a_bad_request()
    {
        var today = RailwayToday();
        using var factory = NewFactory(SafeNow(today));
        var journeyId = await CreateJourneyAsync(factory, today, GuardStaffId, CallingStops(DepartAt(today, 2), 3));

        try
        {
            using var client = factory.CreateApiClient();
            using var response = await PostDepartureAsync(
                client,
                Guid.CreateVersion7(),
                journeyId,
                3,
                DepartAt(today, 2).AddMinutes(22));

            await AssertBadRequestAsync(response, "DepartureLog.StopIsFinalStop");
            Assert.Equal(0, await DepartureLogCountAsync(journeyId));
        }
        finally
        {
            await CleanUpJourneyAsync(journeyId);
        }
    }

    [Fact]
    public async Task A_stop_sequence_that_does_not_exist_is_a_bad_request()
    {
        var today = RailwayToday();
        using var factory = NewFactory(SafeNow(today));
        var journeyId = await CreateJourneyAsync(factory, today, GuardStaffId, CallingStops(DepartAt(today, 2), 3));

        try
        {
            using var client = factory.CreateApiClient();
            using var response = await PostDepartureAsync(
                client,
                Guid.CreateVersion7(),
                journeyId,
                9,
                DepartAt(today, 2).AddMinutes(2));

            await AssertBadRequestAsync(response, "DepartureLog.StopNotFound");
            Assert.Contains("no stop 9", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            Assert.Equal(0, await DepartureLogCountAsync(journeyId));
        }
        finally
        {
            await CleanUpJourneyAsync(journeyId);
        }
    }

    [Fact]
    public async Task A_future_timestamp_is_refused_and_exactly_two_minutes_ahead_is_accepted()
    {
        var today = RailwayToday();
        var now = SafeNow(today);
        using var factory = NewFactory(now);
        var journeyId = await CreateJourneyAsync(factory, today, GuardStaffId, CallingStops(DepartAt(today, 2), 3));
        var auditBaseline = await MaxAuditPositionAsync();

        try
        {
            using var client = factory.CreateApiClient();

            using var tooFar = await PostDepartureAsync(
                client,
                Guid.CreateVersion7(),
                journeyId,
                1,
                now.AddMinutes(10));

            await AssertBadRequestAsync(tooFar, "DepartureLog.TooFarAhead");
            Assert.Equal(0, await DepartureLogCountAsync(journeyId));

            using var allowed = await PostDepartureAsync(
                client,
                Guid.CreateVersion7(),
                journeyId,
                1,
                now.AddMinutes(2));

            Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
        }
        finally
        {
            await CleanUpJourneyAsync(journeyId);
            await ResetAuditEntriesAsync(auditBaseline);
        }
    }

    [Fact]
    public async Task A_timestamp_without_an_offset_is_a_bad_request()
    {
        var today = RailwayToday();
        using var factory = NewFactory(SafeNow(today));
        var journeyId = await CreateJourneyAsync(factory, today, GuardStaffId, CallingStops(DepartAt(today, 2), 3));
        var logId = Guid.CreateVersion7();
        var withoutOffset = DepartAt(today, 2)
            .AddMinutes(2)
            .ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);

        try
        {
            using var client = factory.CreateApiClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, DepartureLogsRoute)
            {
                Content = new StringContent(
                    $$"""
                    {
                      "id": "{{logId}}",
                      "journeyId": "{{journeyId}}",
                      "stopSequence": 1,
                      "departedAt": "{{withoutOffset}}"
                    }
                    """,
                    Encoding.UTF8,
                    "application/json"),
            };
            request.WithStaffToken(GuardToken());

            using var response = await client.SendAsync(request);

            await AssertBadRequestAsync(response, "DepartureLog.MissingOffset");
            Assert.Equal(0, await DepartureLogCountAsync(journeyId));
        }
        finally
        {
            await CleanUpJourneyAsync(journeyId);
        }
    }

    [Fact]
    public async Task A_timestamp_before_the_service_date_started_is_a_bad_request()
    {
        var today = RailwayToday();
        using var factory = NewFactory(SafeNow(today));
        var journeyId = await CreateJourneyAsync(factory, today, GuardStaffId, CallingStops(DepartAt(today, 2), 3));

        try
        {
            using var client = factory.CreateApiClient();
            using var response = await PostDepartureAsync(
                client,
                Guid.CreateVersion7(),
                journeyId,
                1,
                DepartAt(today, 2).AddDays(-1));

            await AssertBadRequestAsync(response, "DepartureLog.BeforeServiceDate");
            Assert.Equal(0, await DepartureLogCountAsync(journeyId));
        }
        finally
        {
            await CleanUpJourneyAsync(journeyId);
        }
    }

    [Fact]
    public async Task A_departure_earlier_than_a_preceding_stop_is_out_of_order()
    {
        var today = RailwayToday();
        var now = SafeNow(today);
        using var factory = NewFactory(now);
        var journeyId = await CreateJourneyAsync(factory, today, GuardStaffId, CallingStops(DepartAt(today, 2), 4));
        var auditBaseline = await MaxAuditPositionAsync();

        try
        {
            using var client = factory.CreateApiClient();

            using var earlierStop = await PostDepartureAsync(
                client,
                Guid.CreateVersion7(),
                journeyId,
                2,
                now.AddMinutes(-40));

            Assert.Equal(HttpStatusCode.Created, earlierStop.StatusCode);

            using var outOfOrder = await PostDepartureAsync(
                client,
                Guid.CreateVersion7(),
                journeyId,
                3,
                now.AddMinutes(-50));

            await AssertBadRequestAsync(outOfOrder, "DepartureLog.OutOfTimeOrder");
            Assert.Contains("stop 2", await outOfOrder.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            Assert.Equal(1, await DepartureLogCountAsync(journeyId));
        }
        finally
        {
            await CleanUpJourneyAsync(journeyId);
            await ResetAuditEntriesAsync(auditBaseline);
        }
    }

    [Fact]
    public async Task Only_yesterdays_and_todays_journeys_are_open_for_logging()
    {
        var today = RailwayToday();
        var yesterday = today.AddDays(-1);
        using var factory = NewFactory(SafeNow(today));
        await StartAsync(factory);

        var recent = await CreateJourneyAsync(factory, yesterday, GuardStaffId, CallingStops(DepartAt(yesterday, 2), 3));
        var stale = await CreateJourneyAsync(factory, today.AddDays(-2), GuardStaffId, CallingStops(DepartAt(today.AddDays(-2), 2), 3));
        var future = await CreateJourneyAsync(factory, today.AddDays(1), GuardStaffId, CallingStops(DepartAt(today.AddDays(1), 2), 3));
        var auditBaseline = await MaxAuditPositionAsync();

        try
        {
            using var client = factory.CreateApiClient();

            // A queue synced after midnight still syncs yesterday's taps.
            using var accepted = await PostDepartureAsync(
                client,
                Guid.CreateVersion7(),
                recent,
                1,
                DepartAt(yesterday, 2).AddMinutes(2));

            Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);

            using var tooOld = await PostDepartureAsync(
                client,
                Guid.CreateVersion7(),
                stale,
                1,
                DepartAt(today.AddDays(-2), 2).AddMinutes(2));

            await AssertBadRequestAsync(tooOld, "DepartureLog.JourneyDateClosed");
            Assert.Equal(0, await DepartureLogCountAsync(stale));

            using var notYet = await PostDepartureAsync(
                client,
                Guid.CreateVersion7(),
                future,
                1,
                DepartAt(today.AddDays(1), 2).AddMinutes(2));

            await AssertBadRequestAsync(notYet, "DepartureLog.JourneyDateClosed");
            Assert.Equal(0, await DepartureLogCountAsync(future));
        }
        finally
        {
            await CleanUpJourneyAsync(recent);
            await CleanUpJourneyAsync(stale);
            await CleanUpJourneyAsync(future);
            await ResetAuditEntriesAsync(auditBaseline);
        }
    }

    [Fact]
    public async Task Cancelled_and_terminated_journeys_accept_the_log_and_keep_their_status()
    {
        var today = RailwayToday();
        var now = SafeNow(today);
        using var factory = NewFactory(now);
        await StartAsync(factory);

        var cancelled = await CreateJourneyAsync(factory, today, GuardStaffId, CallingStops(DepartAt(today, 2), 3));
        var terminated = await CreateJourneyAsync(factory, today, GuardStaffId, CallingStops(DepartAt(today, 2), 4));
        var auditBaseline = await MaxAuditPositionAsync();

        try
        {
            using var client = factory.CreateApiClient();

            await PublishCancellationAsync(client, cancelled);

            using var cancelledLog = await PostDepartureAsync(
                client,
                Guid.CreateVersion7(),
                cancelled,
                1,
                DepartAt(today, 2).AddMinutes(2));

            Assert.Equal(HttpStatusCode.Created, cancelledLog.StatusCode);
            Assert.Equal("cancelled", await StatusAsync(cancelled));

            // A termination needs a running journey: the first departure starts it.
            using var started = await PostDepartureAsync(
                client,
                Guid.CreateVersion7(),
                terminated,
                1,
                DepartAt(today, 2).AddMinutes(2));

            Assert.Equal(HttpStatusCode.Created, started.StatusCode);
            await PublishTerminationAsync(client, terminated, lastStopSequence: 2);

            using var terminatedLog = await PostDepartureAsync(
                client,
                Guid.CreateVersion7(),
                terminated,
                2,
                DepartAt(today, 2).AddMinutes(12));

            Assert.Equal(HttpStatusCode.Created, terminatedLog.StatusCode);
            Assert.Equal("terminated", await StatusAsync(terminated));
        }
        finally
        {
            await CleanUpJourneyAsync(cancelled);
            await CleanUpJourneyAsync(terminated);
            await ResetAuditEntriesAsync(auditBaseline);
        }
    }

    [Fact]
    public async Task All_seven_delay_causes_and_no_cause_are_accepted_and_an_unknown_one_is_refused()
    {
        var today = RailwayToday();
        using var factory = NewFactory(SafeNow(today));
        var departure = DepartAt(today, 2);
        var journeyId = await CreateJourneyAsync(factory, today, GuardStaffId, CallingStops(departure, 9));
        var auditBaseline = await MaxAuditPositionAsync();
        string?[] causes =
        [
            "signalling",
            "rolling_stock",
            "crew",
            "passenger_boarding",
            "infrastructure",
            "weather",
            "other",
            null,
        ];

        try
        {
            using var client = factory.CreateApiClient();

            for (var index = 0; index < causes.Length; index++)
            {
                using var accepted = await PostDepartureAsync(
                    client,
                    Guid.CreateVersion7(),
                    journeyId,
                    index + 1,
                    departure.AddMinutes(2 + (index * 2)),
                    causes[index]);

                Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);

                var body = await ReadLogAsync(accepted);
                Assert.Equal(causes[index], body.DelayCause);
            }

            var refusedJourney = await CreateJourneyAsync(factory, today, GuardStaffId, CallingStops(departure, 3));

            try
            {
                using var refused = await PostDepartureAsync(
                    client,
                    Guid.CreateVersion7(),
                    refusedJourney,
                    1,
                    departure.AddMinutes(2),
                    "accident");

                await AssertBadRequestAsync(refused, "DepartureLog.UnknownDelayCause");
                Assert.Equal(0, await DepartureLogCountAsync(refusedJourney));
            }
            finally
            {
                await CleanUpJourneyAsync(refusedJourney);
            }
        }
        finally
        {
            await CleanUpJourneyAsync(journeyId);
            await ResetAuditEntriesAsync(auditBaseline);
        }
    }

    [Fact]
    public async Task A_log_for_another_guards_journey_is_forbidden_and_the_denial_is_announced_and_audited()
    {
        var today = RailwayToday();
        using var factory = NewFactory(SafeNow(today));
        var journeyId = await CreateJourneyAsync(factory, today, GuardStaffId, CallingStops(DepartAt(today, 2), 3));
        var auditBaseline = await MaxAuditPositionAsync();
        var outboxBaseline = await MaxIdentityOutboxPositionAsync();
        var logId = Guid.CreateVersion7();

        try
        {
            using var client = factory.CreateApiClient();
            using var response = await PostDepartureAsync(
                client,
                logId,
                journeyId,
                1,
                DepartAt(today, 2).AddMinutes(2),
                token: TestTokens.Create(SecondGuardSubject, Roles.Guard));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(0, await DepartureLogCountAsync(journeyId));

            // The handler's own refusal is announced through Identity's outbox. The row is committed
            // by the request transaction even though the answer is a 403 (decision D-9).
            var denialEventId = await NewestDenialMessageAsync(outboxBaseline);
            Assert.NotNull(denialEventId);

            var denial = await WaitForDenialAsync(denialEventId.Value);

            Assert.Equal(SecondGuardStaffId, denial.Actor);
            Assert.Equal(Roles.Guard, denial.Role);
            Assert.Equal("LogDeparture", denial.Action);
            Assert.Equal("denied", denial.Outcome);
        }
        finally
        {
            await CleanUpJourneyAsync(journeyId);
            await RemoveDenialMessagesAsync(outboxBaseline);
            await ResetAuditEntriesAsync(auditBaseline);
        }
    }

    [Fact]
    public async Task A_controller_or_auditor_token_is_forbidden()
    {
        var today = RailwayToday();
        using var factory = NewFactory(SafeNow(today));
        var journeyId = await CreateJourneyAsync(factory, today, GuardStaffId, CallingStops(DepartAt(today, 2), 3));
        var auditBaseline = await MaxAuditPositionAsync();
        var outboxBaseline = await MaxIdentityOutboxPositionAsync();

        try
        {
            using var client = factory.CreateApiClient();

            using var controller = await PostDepartureAsync(
                client,
                Guid.CreateVersion7(),
                journeyId,
                1,
                DepartAt(today, 2).AddMinutes(2),
                token: TestTokens.Create(ControllerSubject, Roles.NetworkController));

            using var auditor = await PostDepartureAsync(
                client,
                Guid.CreateVersion7(),
                journeyId,
                1,
                DepartAt(today, 2).AddMinutes(3),
                token: TestTokens.Create(AuditorSubject, Roles.Auditor));

            Assert.Equal(HttpStatusCode.Forbidden, controller.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, auditor.StatusCode);
            Assert.Equal(0, await DepartureLogCountAsync(journeyId));

            // Both refusals are announced by the authorisation handler and recorded by the bus.
            await WaitForNewEntriesAsync(auditBaseline, expected: 2);
        }
        finally
        {
            await CleanUpJourneyAsync(journeyId);
            await RemoveDenialMessagesAsync(outboxBaseline);
            await ResetAuditEntriesAsync(auditBaseline);
        }
    }

    [Fact]
    public async Task A_request_with_no_token_is_unauthorized()
    {
        var today = RailwayToday();
        using var factory = NewFactory(SafeNow(today));
        var journeyId = await CreateJourneyAsync(factory, today, GuardStaffId, CallingStops(DepartAt(today, 2), 3));

        try
        {
            using var client = factory.CreateApiClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, DepartureLogsRoute)
            {
                Content = JsonContent.Create(new
                {
                    id = Guid.CreateVersion7(),
                    journeyId,
                    stopSequence = 1,
                    departedAt = DepartAt(today, 2).AddMinutes(2).ToString("O", CultureInfo.InvariantCulture),
                }),
            };

            using var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(0, await DepartureLogCountAsync(journeyId));
        }
        finally
        {
            await CleanUpJourneyAsync(journeyId);
        }
    }

    [Fact]
    public async Task An_unknown_journey_is_not_found()
    {
        using var factory = NewFactory(SafeNow(RailwayToday()));
        await StartAsync(factory);

        using var client = factory.CreateApiClient();
        using var response = await PostDepartureAsync(
            client,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            1,
            DateTimeOffset.UtcNow.AddMinutes(-5));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>
    /// The key point of the shared transaction: the audit entry commits with the log or the log does
    /// not happen. The recorder throws after the log, the journey event and its outbox row were
    /// written, so the request transaction has to roll all three back.
    /// </summary>
    [Fact]
    public async Task A_failing_audit_step_answers_500_and_leaves_no_log_event_outbox_row_or_state_change()
    {
        var today = RailwayToday();
        var now = SafeNow(today);
        using var factory = NewFactory(
            now,
            services =>
            {
                services.RemoveAll<IAuditRecorder>();
                services.AddScoped<IAuditRecorder, ThrowingAuditRecorder>();
            });

        var journeyId = await CreateJourneyAsync(factory, today, GuardStaffId, CallingStops(DepartAt(today, 2), 3));

        try
        {
            using var client = factory.CreateApiClient();
            using var response = await PostDepartureAsync(
                client,
                Guid.CreateVersion7(),
                journeyId,
                1,
                DepartAt(today, 2).AddMinutes(2));

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

            Assert.Equal(0, await DepartureLogCountAsync(journeyId));
            Assert.Equal(0, await CountAsync(
                $"SELECT count(*) FROM operations.journey_events WHERE journey_id = '{journeyId}'"));
            Assert.Equal(0, await CountAsync(
                $"SELECT count(*) FROM operations.outbox_messages WHERE ordering_key = '{journeyId}'"));
            Assert.Null(await ActualDepartureAsync(journeyId, 1));
        }
        finally
        {
            await CleanUpJourneyAsync(journeyId);
        }
    }

    private SmartRailApiFactory NewFactory(
        DateTimeOffset? now = null,
        Action<IServiceCollection>? configureServices = null) =>
        new(
            _postgres.ConnectionString,
            configureServices: services =>
            {
                if (now is { } fixedNow)
                {
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton<TimeProvider>(new FixedTimeProvider(fixedNow));
                }

                configureServices?.Invoke(services);
            });

    private static string GuardToken() => TestTokens.Create(GuardSubject, Roles.Guard);

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

    private static DateTimeOffset DepartAt(DateOnly serviceDate, int hour) =>
        new(serviceDate.ToDateTime(new TimeOnly(hour, 0)), TimeSpan.Zero);

    /// <summary>Calling stops from the given departure, each ten minutes apart; the last has no departure.</summary>
    private static List<JourneyStopDto> CallingStops(DateTimeOffset departure, int count)
    {
        var stops = new List<JourneyStopDto>(count);

        for (var sequence = 1; sequence <= count; sequence++)
        {
            var arrival = sequence == 1 ? (DateTimeOffset?)null : departure.AddMinutes((sequence - 1) * 10);
            var departs = sequence == count ? (DateTimeOffset?)null : departure.AddMinutes((sequence - 1) * 11);

            stops.Add(new JourneyStopDto(sequence, Guid.CreateVersion7(), true, arrival, departs, Guid.CreateVersion7()));
        }

        return stops;
    }

    private static async Task StartAsync(SmartRailApiFactory factory)
    {
        using var client = factory.CreateApiClient();
        using var response = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<Guid> CreateJourneyAsync(
        SmartRailApiFactory factory,
        DateOnly serviceDate,
        Guid guardStaffId,
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
                guardStaffId,
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

    private static async Task<HttpResponseMessage> PostDepartureAsync(
        HttpClient client,
        Guid logId,
        Guid journeyId,
        int stopSequence,
        DateTimeOffset departedAt,
        string? delayCause = null,
        string? token = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, DepartureLogsRoute)
        {
            Content = JsonContent.Create(new
            {
                id = logId,
                journeyId,
                stopSequence,
                departedAt = departedAt.ToString("O", CultureInfo.InvariantCulture),
                delayCause,
            }),
        };
        request.WithStaffToken(token ?? GuardToken());

        return await client.SendAsync(request);
    }

    private static async Task PublishCancellationAsync(HttpClient client, Guid journeyId)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{ApiRoutes.BasePath}/operations/journeys/{journeyId}/cancellation");
        request.WithStaffToken(TestTokens.Create(ControllerSubject, Roles.NetworkController));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task PublishTerminationAsync(HttpClient client, Guid journeyId, int lastStopSequence)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{ApiRoutes.BasePath}/operations/journeys/{journeyId}/termination")
        {
            Content = JsonContent.Create(new { lastStopSequence }),
        };
        request.WithStaffToken(TestTokens.Create(ControllerSubject, Roles.NetworkController));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task<LogItem> ReadLogAsync(HttpResponseMessage response)
    {
        var log = await response.Content.ReadFromJsonAsync<LogItem>();
        Assert.NotNull(log);
        return log;
    }

    private static async Task AssertBadRequestAsync(HttpResponseMessage response, string errorCode)
    {
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(
            body.Contains(errorCode, StringComparison.OrdinalIgnoreCase),
            $"The 400 response does not name '{errorCode}': {body}");
    }

    private async Task<int> CountAsync(string sql)
    {
        var count = Assert.Single(await _postgres.QueryAsync(sql, reader => reader.GetInt64(0)));
        return (int)count;
    }

    private async Task<int> DepartureLogCountAsync(Guid journeyId) =>
        await CountAsync($"SELECT count(*) FROM operations.departure_logs WHERE journey_id = '{journeyId}'");

    private async Task<DateTimeOffset?> ActualDepartureAsync(Guid journeyId, int stopSequence)
    {
        var values = await _postgres.QueryAsync(
            $"""
            SELECT actual_departure
            FROM operations.journey_stop_live_states
            WHERE journey_id = '{journeyId}' AND sequence = {stopSequence}
            """,
            reader => reader.IsDBNull(0) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(0));

        return Assert.Single(values);
    }

    private async Task<string> StatusAsync(Guid journeyId)
    {
        var statuses = await _postgres.QueryAsync(
            $"SELECT lifecycle_status FROM operations.journey_live_states WHERE journey_id = '{journeyId}'",
            reader => reader.GetString(0));

        return Assert.Single(statuses);
    }

    /// <summary>One log row, one journey event, one outbox row and one success entry: no replay wrote anything.</summary>
    private async Task AssertOnlyOneOfEverythingAsync(Guid journeyId)
    {
        Assert.Equal(1, await DepartureLogCountAsync(journeyId));
        Assert.Equal(1, await CountAsync(
            $"SELECT count(*) FROM operations.journey_events WHERE journey_id = '{journeyId}'"));
        Assert.Equal(1, await CountAsync(
            $"SELECT count(*) FROM operations.outbox_messages WHERE ordering_key = '{journeyId}'"));
        Assert.Equal(1, await CountAsync(
            $"SELECT count(*) FROM audit.entries WHERE action = 'LogDeparture' AND target_id = '{journeyId}'"));
    }

    private async Task<AuditRow> ReadSuccessEntryAsync(Guid journeyId)
    {
        var entries = await _postgres.QueryAsync(
            $"""
            SELECT actor_staff_id, actor_role, action, target_type, target_id, outcome,
                   before::text, after::text
            FROM audit.entries
            WHERE action = 'LogDeparture' AND target_id = '{journeyId}'
            """,
            reader => new AuditRow(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7)));

        return Assert.Single(entries);
    }

    private async Task<long> MaxAuditPositionAsync() =>
        Assert.Single(await _postgres.QueryAsync(
            "SELECT coalesce(max(chain_position), 0) FROM audit.entries",
            reader => reader.GetInt64(0)));

    private async Task<long> MaxIdentityOutboxPositionAsync() =>
        Assert.Single(await _postgres.QueryAsync(
            "SELECT coalesce(max(position), 0) FROM identity.outbox_messages",
            reader => reader.GetInt64(0)));

    /// <summary>The denial message this request published, so the test can wait for it and remove it.</summary>
    private async Task<Guid?> NewestDenialMessageAsync(long baselinePosition)
    {
        var ids = await _postgres.QueryAsync(
            $"""
            SELECT id
            FROM identity.outbox_messages
            WHERE event_type = 'AccessDenied' AND position > {baselinePosition}
            ORDER BY position DESC
            LIMIT 1
            """,
            reader => reader.GetGuid(0));

        return ids.Count > 0 ? ids[0] : null;
    }

    /// <summary>
    /// Waits for the denial this request announced to reach the audit record. Delivery is
    /// asynchronous, so the entry appears shortly after the 403 (audit design D5), and the event
    /// identifier tells this denial apart from any other the run recorded.
    /// </summary>
    private async Task<DenialRow> WaitForDenialAsync(Guid sourceEventId)
    {
        var deadline = DateTimeOffset.UtcNow.Add(Timeout);

        while (true)
        {
            var rows = await _postgres.QueryAsync(
                $"""
                SELECT actor_staff_id, actor_role, action, outcome
                FROM audit.entries
                WHERE source_event_id = '{sourceEventId}'
                """,
                reader => new DenialRow(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3)));

            if (rows.Count > 0)
            {
                return rows[0];
            }

            Assert.True(
                DateTimeOffset.UtcNow < deadline,
                $"The refusal was not recorded within {Timeout}.");

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
    }

    /// <summary>
    /// Waits for the denials the authorisation handler announced to reach the audit record, so the
    /// cleanup below cannot race a late delivery.
    /// </summary>
    private async Task WaitForNewEntriesAsync(long baselinePosition, int expected)
    {
        var deadline = DateTimeOffset.UtcNow.Add(Timeout);

        while (await CountAsync(
                   $"SELECT count(*) FROM audit.entries WHERE chain_position > {baselinePosition}") < expected)
        {
            Assert.True(
                DateTimeOffset.UtcNow < deadline,
                $"The refusals were not recorded within {Timeout}.");

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
    }

    private async Task RemoveDenialMessagesAsync(long baselinePosition)
    {
        await _postgres.ExecuteAsync(
            $"""
            DELETE FROM identity.outbox_deliveries
            WHERE message_id IN (
                SELECT id FROM identity.outbox_messages
                WHERE event_type = 'AccessDenied' AND position > {baselinePosition})
            """);

        await _postgres.ExecuteAsync(
            $"""
            DELETE FROM identity.outbox_messages
            WHERE event_type = 'AccessDenied' AND position > {baselinePosition}
            """);
    }

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

    /// <summary>A clock frozen at one instant, so a receipt time and the future bound are exact.</summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>An audit recorder that fails at the last step of the logging use case.</summary>
    private sealed class ThrowingAuditRecorder : IAuditRecorder
    {
        public Task<Guid> RecordAsync(
            AuditRecordRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Injected audit failure after the log and its event were written.");
    }

    private sealed record LogItem(
        Guid Id,
        Guid JourneyId,
        int StopSequence,
        DateTimeOffset DepartedAt,
        string? DelayCause,
        DateTimeOffset ReceivedAt);

    private sealed record JourneyEventRow(
        string Type,
        Guid DepartureLogId,
        DateTimeOffset OccurredAt,
        DateTimeOffset RecordedAt);

    private sealed record AuditRow(
        Guid Actor,
        string Role,
        string Action,
        string TargetType,
        string TargetId,
        string Outcome,
        string? Before,
        string? After);

    private sealed record DenialRow(Guid Actor, string Role, string Action, string Outcome);
}
