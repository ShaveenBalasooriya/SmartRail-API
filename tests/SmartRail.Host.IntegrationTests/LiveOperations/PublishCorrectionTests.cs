using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartRail.Audit.Contracts;
using SmartRail.Host.Http;
using SmartRail.Host.IntegrationTests.Infrastructure;
using SmartRail.LiveOperations.Contracts;
using SmartRail.RailNetwork.Contracts;
using SmartRail.RailNetwork.Infrastructure.Persistence;
using SmartRail.SharedKernel.Application.Abstractions;
using SmartRail.SharedKernel.Application.Events;

namespace SmartRail.Host.IntegrationTests.LiveOperations;

/// <summary>
/// Correcting a published change through <c>POST /operations/changes/{changeId}/corrections</c>
/// (change-correction spec, controller-operations task 3.1, design D3, D4 and D7): a wrong
/// cancellation is withdrawn and the journey returns to its uncorrected state, later changes survive,
/// a wrong termination and a wrong delay are reverted, a correction cannot be corrected twice or be
/// corrected itself, the shared today rule and the completed-journey rule apply, the
/// <c>correction_published</c> event names the original change and its event, the audit entry links
/// to the original's unchanged entry the Auditor can read, the controller-only policy with its denied
/// entry, the audit step failing and two simultaneous corrections of one change.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PublishCorrectionTests
{
    /// <summary>The seeded controller account (seed/identity/staff.json).</summary>
    private static readonly Guid ControllerStaffId = Guid.Parse("01a15874-e69c-70da-a689-76a7268ed330");

    /// <summary>The seeded guard account (seed/identity/staff.json).</summary>
    private static readonly Guid GuardStaffId = Guid.Parse("01a15874-e69d-73b2-b24d-d85d22fdead2");

    /// <summary>How long a denial may take to be delivered and recorded before the test fails.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private readonly PostgresFixture _postgres;

    public PublishCorrectionTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task A_wrong_cancellation_is_withdrawn_and_both_changes_stay_in_the_history()
    {
        using var factory = NewFactory();
        var journeyId = await CreateTodayJourneyAsync(factory, RailwayToday());
        var auditBaseline = await MaxAuditPositionAsync();

        try
        {
            using var client = factory.CreateApiClient();
            using var cancelled = await PostCancellationAsync(client, journeyId);
            Assert.Equal(HttpStatusCode.Created, cancelled.StatusCode);

            var cancellation = await cancelled.Content.ReadFromJsonAsync<ChangeItem>();
            Assert.NotNull(cancellation);

            // No body is bound: a correction with no body is the plain form of the call.
            using var corrected = await PostCorrectionAsync(client, cancellation.Id);

            Assert.Equal(HttpStatusCode.Created, corrected.StatusCode);

            var correction = await corrected.Content.ReadFromJsonAsync<ChangeItem>();
            Assert.NotNull(correction);

            // The response is the correction itself: type, the original it withdraws, publisher,
            // publish time and the provenance label (status-publication spec: response content).
            Assert.NotEqual(Guid.Empty, correction.Id);
            Assert.Equal("correction", correction.Type);
            Assert.Equal(cancellation.Id.ToString(), correction.Values["correctsChangeId"]);
            Assert.Equal("Mahesh Perera", correction.PublisherName);
            Assert.Equal("automatically_published", correction.Provenance);
            Assert.False(correction.Corrected);

            // The journey is no longer cancelled: with no departure fact on record it is its
            // scheduled self, which is exactly what "as if never published" means (design D3).
            var detail = await GetDetailAsync(client, journeyId);

            Assert.Equal("scheduled", detail.Journey.Status);

            // Both changes are in the history, the original first and marked corrected, and the
            // correction names it (NC-06, S-12).
            Assert.Equal(2, detail.Changes.Count);
            Assert.Equal(cancellation.Id, detail.Changes[0].Id);
            Assert.True(detail.Changes[0].Corrected);
            Assert.Equal(correction.Id, detail.Changes[1].Id);
            Assert.Equal(cancellation.Id.ToString(), detail.Changes[1].Values["correctsChangeId"]);
            Assert.False(detail.Changes[1].Corrected);

            var stored = await _postgres.QueryAsync(
                $"""
                SELECT change_type, corrects_change_id
                FROM operations.status_changes
                WHERE journey_id = '{journeyId}'
                ORDER BY fact_order
                """,
                reader => (
                    Type: reader.GetString(0),
                    Corrects: reader.IsDBNull(1) ? (Guid?)null : reader.GetGuid(1)));

            Assert.Equal(2, stored.Count);
            Assert.Equal("cancellation", stored[0].Type);
            Assert.Equal("correction", stored[1].Type);
            Assert.Equal(cancellation.Id, stored[1].Corrects);

            // The correction's audit entry reverses the cancellation: the status it reverted and the
            // status the journey is left in (design D4).
            var entry = Assert.Single(await _postgres.QueryAsync(
                $"""
                SELECT before::text, after::text
                FROM audit.entries
                WHERE target_id = '{journeyId}' AND action = 'PublishCorrection'
                """,
                reader => new AuditValues(
                    reader.IsDBNull(0) ? null : reader.GetString(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1))));

            using var before = JsonDocument.Parse(entry.Before!);
            Assert.Equal("cancelled", before.RootElement.GetProperty("status").GetString());

            using var after = JsonDocument.Parse(entry.After!);
            Assert.Equal("scheduled", after.RootElement.GetProperty("status").GetString());
        }
        finally
        {
            await CleanUpJourneyAsync(journeyId);
            await ResetAuditEntriesAsync(auditBaseline);
        }
    }

    [Fact]
    public async Task A_later_platform_change_survives_the_correction_of_an_earlier_one()
    {
        var network = new TestNetwork();
        using var factory = NewFactory(network);
        var today = RailwayToday();
        var journeyId = await CreateJourneyAsync(factory, today, network.Stops(today));
        var auditBaseline = await MaxAuditPositionAsync();

        try
        {
            using var client = factory.CreateApiClient();

            // Platform changes A then B on one stop: the stop starts on "1", A moves it to "2" and
            // B moves it to "3".
            using var movedToA = await PostPlatformChangeAsync(client, journeyId, 2, network.BetaTwo);
            Assert.Equal(HttpStatusCode.Created, movedToA.StatusCode);

            var changeA = await movedToA.Content.ReadFromJsonAsync<ChangeItem>();
            Assert.NotNull(changeA);

            using var movedToB = await PostPlatformChangeAsync(client, journeyId, 2, network.BetaThree);
            Assert.Equal(HttpStatusCode.Created, movedToB.StatusCode);

            using var corrected = await PostCorrectionAsync(client, changeA.Id, new { });
            Assert.Equal(HttpStatusCode.Created, corrected.StatusCode);

            var correction = await corrected.Content.ReadFromJsonAsync<ChangeItem>();
            Assert.NotNull(correction);

            // A correction reverts only the change it names: B is still in force, so the stop shows
            // B's platform (change-correction spec: later change survives).
            var detail = await GetDetailAsync(client, journeyId);

            Assert.Equal(new PlatformItem(network.BetaThree, "3"), detail.Stops[1].Platform);

            Assert.True(Assert.Single(detail.Changes, change => change.Id == changeA.Id).Corrected);
            Assert.False(Assert.Single(detail.Changes, change => change.Type == "platform_change"
                && change.Id != changeA.Id).Corrected);

            // This correction changes no live state, and still produces its event: every published
            // change produces exactly one journey event, which the channels apply by id and
            // sequence (change-correction spec; docs/data-model.md).
            var events = await ReadEventsAsync(factory, journeyId);

            Assert.Equal(3, events.Count);

            var originalEventId = Assert.Single(await _postgres.QueryAsync(
                $"""
                SELECT id
                FROM operations.journey_events
                WHERE journey_id = '{journeyId}' AND status_change_id = '{changeA.Id}'
                """,
                reader => reader.GetGuid(0)));

            var correctionEvent = Assert.Single(
                events,
                journeyEvent => journeyEvent.Type == JourneyEventType.CorrectionPublished);

            Assert.Equal(correction.Id, correctionEvent.SourceId);
            Assert.Equal(changeA.Id, correctionEvent.CorrectsChangeId);
            Assert.Equal(originalEventId, correctionEvent.CorrectsEventId);

            // The announcement rides the same transaction: one outbox row per event, including the
            // correction's.
            var outboxIds = await _postgres.QueryAsync(
                $"SELECT id FROM operations.outbox_messages WHERE ordering_key = '{journeyId}'",
                reader => reader.GetGuid(0));

            Assert.Equal(3, outboxIds.Count);
            Assert.Contains(correctionEvent.Id, outboxIds);

            // The audit entry's after value is read from the recomputed state, so it names the
            // platform still in force (B's), not the one the original replaced.
            var entry = Assert.Single(await _postgres.QueryAsync(
                $"""
                SELECT before::text, after::text
                FROM audit.entries
                WHERE target_id = '{journeyId}' AND action = 'PublishCorrection'
                """,
                reader => new AuditValues(
                    reader.IsDBNull(0) ? null : reader.GetString(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1))));

            using var before = JsonDocument.Parse(entry.Before!);
            Assert.Equal("2", before.RootElement.GetProperty("platform").GetString());

            using var after = JsonDocument.Parse(entry.After!);
            Assert.Equal("3", after.RootElement.GetProperty("platform").GetString());
        }
        finally
        {
            await CleanUpJourneyAsync(journeyId);
            await ResetAuditEntriesAsync(auditBaseline);
        }
    }

    [Fact]
    public async Task A_corrected_delay_reports_the_later_delay_still_in_force_as_its_after_value()
    {
        using var factory = NewFactory();
        var journeyId = await CreateTodayJourneyAsync(factory, RailwayToday());
        var auditBaseline = await MaxAuditPositionAsync();

        try
        {
            using var client = factory.CreateApiClient();

            // Delay 30 then delay 10 on the same stop: the later one is the delay in force.
            using var delayed = await PostDelayAsync(client, journeyId, fromStopSequence: 2, minutes: 30);
            Assert.Equal(HttpStatusCode.Created, delayed.StatusCode);

            var delay30 = await delayed.Content.ReadFromJsonAsync<ChangeItem>();
            Assert.NotNull(delay30);

            using var later = await PostDelayAsync(client, journeyId, fromStopSequence: 2, minutes: 10);
            Assert.Equal(HttpStatusCode.Created, later.StatusCode);

            using var corrected = await PostCorrectionAsync(client, delay30.Id);
            Assert.Equal(HttpStatusCode.Created, corrected.StatusCode);

            // The correction's after value comes from the state the sink just recomputed, so it is
            // the delay still in force rather than the corrected one (design D4).
            var entry = Assert.Single(await _postgres.QueryAsync(
                $"""
                SELECT before::text, after::text
                FROM audit.entries
                WHERE target_id = '{journeyId}' AND action = 'PublishCorrection'
                """,
                reader => new AuditValues(
                    reader.IsDBNull(0) ? null : reader.GetString(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1))));

            using var after = JsonDocument.Parse(entry.After!);
            Assert.Equal(10, after.RootElement.GetProperty("delayMinutes").GetInt32());

            // The later delay is still what passengers see at that stop.
            var detail = await GetDetailAsync(client, journeyId);
            Assert.Equal(
                detail.Stops[1].ScheduledDeparture!.Value.AddMinutes(10),
                detail.Stops[1].ExpectedDeparture);
        }
        finally
        {
            await CleanUpJourneyAsync(journeyId);
            await ResetAuditEntriesAsync(auditBaseline);
        }
    }

    /// <summary>
    /// The spec's "wrong early termination" scenario. The departure facts are recorded by
    /// <c>add-departure-logging</c>, which is not built yet, so this journey is made running by
    /// writing its stored state; after the correction the recomputation sees no departure fact and
    /// returns the journey's uncorrected, scheduled self, with expected times equal to the scheduled
    /// times those departures were at. The calculator's "corrected termination gives running again"
    /// rule already has its unit coverage from task 2.6.
    /// </summary>
    [Fact]
    public async Task A_wrong_early_termination_is_withdrawn_and_the_journey_returns_to_its_uncorrected_state()
    {
        using var factory = NewFactory();
        var today = RailwayToday();
        var journeyId = await CreateJourneyAsync(factory, today, FiveCallingStops(today));
        var auditBaseline = await MaxAuditPositionAsync();

        try
        {
            await MakeRunningAsync(journeyId, today, 1, 2);

            using var client = factory.CreateApiClient();
            using var terminated = await PostTerminationAsync(client, journeyId, lastStopSequence: 3);
            Assert.Equal(HttpStatusCode.Created, terminated.StatusCode);

            var termination = await terminated.Content.ReadFromJsonAsync<ChangeItem>();
            Assert.NotNull(termination);

            Assert.Equal("terminated", (await GetDetailAsync(client, journeyId)).Journey.Status);

            using var corrected = await PostCorrectionAsync(client, termination.Id);
            Assert.Equal(HttpStatusCode.Created, corrected.StatusCode);

            var detail = await GetDetailAsync(client, journeyId);

            Assert.Equal("scheduled", detail.Journey.Status);
            Assert.All(detail.Stops, stop =>
            {
                Assert.Equal(stop.ScheduledArrival, stop.ExpectedArrival);
                Assert.Equal(stop.ScheduledDeparture, stop.ExpectedDeparture);
            });
        }
        finally
        {
            await CleanUpJourneyAsync(journeyId);
            await ResetAuditEntriesAsync(auditBaseline);
        }
    }

    [Fact]
    public async Task A_corrected_delay_restores_the_expected_times_the_departures_give()
    {
        using var factory = NewFactory();
        var today = RailwayToday();
        var journeyId = await CreateTodayJourneyAsync(factory, today);
        var auditBaseline = await MaxAuditPositionAsync();

        try
        {
            using var client = factory.CreateApiClient();
            using var delayed = await PostDelayAsync(client, journeyId, fromStopSequence: 2, minutes: 30);
            Assert.Equal(HttpStatusCode.Created, delayed.StatusCode);

            var delay = await delayed.Content.ReadFromJsonAsync<ChangeItem>();
            Assert.NotNull(delay);

            var delayedDetail = await GetDetailAsync(client, journeyId);
            Assert.Equal(
                delayedDetail.Stops[1].ScheduledDeparture!.Value.AddMinutes(30),
                delayedDetail.Stops[1].ExpectedDeparture);

            using var corrected = await PostCorrectionAsync(client, delay.Id);
            Assert.Equal(HttpStatusCode.Created, corrected.StatusCode);

            // Expected times return to what the departures alone give: with no departure fact on
            // record that is the scheduled time itself.
            var detail = await GetDetailAsync(client, journeyId);

            Assert.All(detail.Stops, stop =>
            {
                Assert.Equal(stop.ScheduledArrival, stop.ExpectedArrival);
                Assert.Equal(stop.ScheduledDeparture, stop.ExpectedDeparture);
            });

            // The audit entry's before and after are the delay at the original's first stop either
            // side of the correction (design D4).
            var entry = Assert.Single(await _postgres.QueryAsync(
                $"""
                SELECT before::text, after::text
                FROM audit.entries
                WHERE target_id = '{journeyId}' AND action = 'PublishCorrection'
                """,
                reader => new AuditValues(
                    reader.IsDBNull(0) ? null : reader.GetString(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1))));

            using var before = JsonDocument.Parse(entry.Before!);
            Assert.Equal(30, before.RootElement.GetProperty("delayMinutes").GetInt32());

            using var after = JsonDocument.Parse(entry.After!);
            Assert.Equal(0, after.RootElement.GetProperty("delayMinutes").GetInt32());
        }
        finally
        {
            await CleanUpJourneyAsync(journeyId);
            await ResetAuditEntriesAsync(auditBaseline);
        }
    }

    [Fact]
    public async Task A_change_that_has_already_been_corrected_is_a_conflict()
    {
        using var factory = NewFactory();
        var journeyId = await CreateTodayJourneyAsync(factory, RailwayToday());
        var auditBaseline = await MaxAuditPositionAsync();

        try
        {
            using var client = factory.CreateApiClient();
            using var cancelled = await PostCancellationAsync(client, journeyId);
            var cancellation = await cancelled.Content.ReadFromJsonAsync<ChangeItem>();
            Assert.NotNull(cancellation);

            using var first = await PostCorrectionAsync(client, cancellation.Id);
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);

            using var second = await PostCorrectionAsync(client, cancellation.Id);

            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
            Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);
            Assert.Equal(1, await CountAsync(
                $"""
                SELECT count(*)
                FROM operations.status_changes
                WHERE journey_id = '{journeyId}' AND corrects_change_id = '{cancellation.Id}'
                """));
        }
        finally
        {
            await CleanUpJourneyAsync(journeyId);
            await ResetAuditEntriesAsync(auditBaseline);
        }
    }

    [Fact]
    public async Task A_correction_cannot_itself_be_corrected()
    {
        using var factory = NewFactory();
        var journeyId = await CreateTodayJourneyAsync(factory, RailwayToday());
        var auditBaseline = await MaxAuditPositionAsync();

        try
        {
            using var client = factory.CreateApiClient();
            using var cancelled = await PostCancellationAsync(client, journeyId);
            var cancellation = await cancelled.Content.ReadFromJsonAsync<ChangeItem>();
            Assert.NotNull(cancellation);

            using var corrected = await PostCorrectionAsync(client, cancellation.Id);
            var correction = await corrected.Content.ReadFromJsonAsync<ChangeItem>();
            Assert.NotNull(correction);

            using var nested = await PostCorrectionAsync(client, correction.Id);

            Assert.Equal(HttpStatusCode.Conflict, nested.StatusCode);
            Assert.Equal(1, await CountAsync(
                $"""
                SELECT count(*)
                FROM operations.status_changes
                WHERE journey_id = '{journeyId}' AND corrects_change_id IS NOT NULL
                """));
        }
        finally
        {
            await CleanUpJourneyAsync(journeyId);
            await ResetAuditEntriesAsync(auditBaseline);
        }
    }

    [Fact]
    public async Task Correcting_an_unknown_change_is_not_found()
    {
        using var factory = NewFactory();
        await StartAsync(factory);

        using var client = factory.CreateApiClient();
        using var response = await PostCorrectionAsync(client, Guid.CreateVersion7());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Correcting_a_change_on_a_completed_journey_is_a_conflict()
    {
        using var factory = NewFactory();
        var journeyId = await CreateTodayJourneyAsync(factory, RailwayToday());
        var auditBaseline = await MaxAuditPositionAsync();

        try
        {
            using var client = factory.CreateApiClient();
            using var cancelled = await PostCancellationAsync(client, journeyId);
            var cancellation = await cancelled.Content.ReadFromJsonAsync<ChangeItem>();
            Assert.NotNull(cancellation);

            await _postgres.ExecuteAsync(
                $"UPDATE operations.journey_live_states SET lifecycle_status = 'completed' WHERE journey_id = '{journeyId}'");

            using var response = await PostCorrectionAsync(client, cancellation.Id);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal(1, await CountAsync(
                $"SELECT count(*) FROM operations.status_changes WHERE journey_id = '{journeyId}'"));
        }
        finally
        {
            await CleanUpJourneyAsync(journeyId);
            await ResetAuditEntriesAsync(auditBaseline);
        }
    }

    [Fact]
    public async Task Correcting_a_change_on_a_journey_dated_tomorrow_is_a_bad_request()
    {
        using var factory = NewFactory();
        var tomorrow = RailwayToday().AddDays(1);
        var journeyId = await CreateJourneyAsync(factory, tomorrow, CallingStops(tomorrow));
        var changeId = Guid.CreateVersion7();

        try
        {
            // A change recorded directly, so the correction gets past the unknown-change check and
            // is refused by the shared today rule.
            await _postgres.ExecuteAsync(
                $"""
                INSERT INTO operations.status_changes
                    (id, journey_id, change_type, provenance, published_by, published_at)
                VALUES
                    ('{changeId}', '{journeyId}', 'cancellation', 'automatically_published',
                     '{ControllerStaffId}', '{DateTimeOffset.UtcNow:O}')
                """);

            using var client = factory.CreateApiClient();
            using var response = await PostCorrectionAsync(client, changeId);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Empty(await _postgres.QueryAsync(
                $"SELECT id FROM operations.status_changes WHERE corrects_change_id = '{changeId}'",
                reader => reader.GetGuid(0)));
        }
        finally
        {
            await CleanUpJourneyAsync(journeyId);
        }
    }

    [Fact]
    public async Task A_correction_event_names_the_original_change_and_its_event()
    {
        using var factory = NewFactory();
        var journeyId = await CreateTodayJourneyAsync(factory, RailwayToday());
        var auditBaseline = await MaxAuditPositionAsync();

        try
        {
            using var client = factory.CreateApiClient();
            using var cancelled = await PostCancellationAsync(client, journeyId);
            var cancellation = await cancelled.Content.ReadFromJsonAsync<ChangeItem>();
            Assert.NotNull(cancellation);

            var originalEventId = Assert.Single(await _postgres.QueryAsync(
                $"""
                SELECT id
                FROM operations.journey_events
                WHERE journey_id = '{journeyId}' AND status_change_id = '{cancellation.Id}'
                """,
                reader => reader.GetGuid(0)));

            using var corrected = await PostCorrectionAsync(client, cancellation.Id);
            var correction = await corrected.Content.ReadFromJsonAsync<ChangeItem>();
            Assert.NotNull(correction);

            using var scope = factory.Services.CreateScope();
            var events = await scope.ServiceProvider
                .GetRequiredService<ILiveJourneys>()
                .GetEventsAfterAsync(0, journeyId);

            Assert.Equal(2, events.Count);

            // Events of other types carry no correction reference.
            var cancellationEvent = Assert.Single(events, journeyEvent => journeyEvent.SourceId == cancellation.Id);
            Assert.Equal(JourneyEventType.CancellationPublished, cancellationEvent.Type);
            Assert.Null(cancellationEvent.CorrectsChangeId);
            Assert.Null(cancellationEvent.CorrectsEventId);

            // The correction names the original change and the event that recorded it (change-
            // correction spec: event references original).
            var correctionEvent = Assert.Single(events, journeyEvent => journeyEvent.SourceId == correction.Id);
            Assert.Equal(JourneyEventType.CorrectionPublished, correctionEvent.Type);
            Assert.Equal(cancellation.Id, correctionEvent.CorrectsChangeId);
            Assert.Equal(originalEventId, correctionEvent.CorrectsEventId);
        }
        finally
        {
            await CleanUpJourneyAsync(journeyId);
            await ResetAuditEntriesAsync(auditBaseline);
        }
    }

    /// <summary>
    /// The correction's audit entry links to the original's entry, which stays untouched - hash
    /// included - and the Auditor can read the link (change-correction spec: linked audit entries).
    /// </summary>
    [Fact]
    public async Task A_correction_links_its_audit_entry_to_the_original_and_leaves_it_unchanged()
    {
        using var factory = NewFactory();
        var journeyId = await CreateTodayJourneyAsync(factory, RailwayToday());
        var auditBaseline = await MaxAuditPositionAsync();

        try
        {
            using var client = factory.CreateApiClient();
            using var cancelled = await PostCancellationAsync(client, journeyId);
            var cancellation = await cancelled.Content.ReadFromJsonAsync<ChangeItem>();
            Assert.NotNull(cancellation);

            var originalEntryId = await ReadAuditEntryIdAsync(cancellation.Id);
            Assert.NotNull(originalEntryId);

            var originalBefore = Assert.Single(await _postgres.QueryAsync(
                $"""
                SELECT chain_position, before::text, after::text, previous_hash, entry_hash
                FROM audit.entries
                WHERE id = '{originalEntryId}'
                """,
                reader => new AuditRow(
                    reader.GetInt64(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4))));

            using var corrected = await PostCorrectionAsync(client, cancellation.Id);
            var correction = await corrected.Content.ReadFromJsonAsync<ChangeItem>();
            Assert.NotNull(correction);

            var correctionEntryId = await ReadAuditEntryIdAsync(correction.Id);
            Assert.NotNull(correctionEntryId);

            var relatedEntryId = Assert.Single(await _postgres.QueryAsync(
                $"SELECT related_entry_id FROM audit.entries WHERE id = '{correctionEntryId}'",
                reader => reader.IsDBNull(0) ? (Guid?)null : reader.GetGuid(0)));

            Assert.Equal(originalEntryId, relatedEntryId);

            // The original entry is untouched: same position, values and hashes.
            var originalAfter = Assert.Single(await _postgres.QueryAsync(
                $"""
                SELECT chain_position, before::text, after::text, previous_hash, entry_hash
                FROM audit.entries
                WHERE id = '{originalEntryId}'
                """,
                reader => new AuditRow(
                    reader.GetInt64(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4))));

            Assert.Equal(originalBefore, originalAfter);

            // The Auditor reads the correction entry in full and sees the link to the original.
            using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    $"{ApiRoutes.BasePath}/audit/entries/{correctionEntryId}")
                .WithStaffToken(TestTokens.Create("auditor.wasantha", Roles.Auditor));

            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(
                originalEntryId,
                document.RootElement.GetProperty("relatedEntryId").GetGuid());
        }
        finally
        {
            await CleanUpJourneyAsync(journeyId);
            await ResetAuditEntriesAsync(auditBaseline);
        }
    }

    [Fact]
    public async Task A_request_with_no_token_is_unauthorized()
    {
        using var factory = NewFactory();
        await StartAsync(factory);

        using var client = factory.CreateApiClient();
        using var response = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, CorrectionRoute(Guid.CreateVersion7())));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("guard.ishara", Roles.Guard, "01a15874-e69d-73b2-b24d-d85d22fdead2")]
    [InlineData("auditor.wasantha", Roles.Auditor, "01a15874-e6a0-7fd3-8c46-34bd36e8f16d")]
    public async Task Only_the_network_controller_may_correct_and_the_denial_is_audited(
        string subject,
        string role,
        string staffId)
    {
        using var factory = NewFactory();
        await StartAsync(factory);

        var auditBaseline = await MaxAuditPositionAsync();
        var outboxBaseline = await MaxIdentityOutboxPositionAsync();
        Guid? denialEventId = null;

        try
        {
            using var client = factory.CreateApiClient();
            using var response = await PostCorrectionAsync(
                client,
                Guid.CreateVersion7(),
                token: TestTokens.Create(subject, role));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

            denialEventId = await NewestDenialMessageAsync(outboxBaseline);

            // A denied write is recorded shortly after the refusal, by the bus (audit D5). Waiting
            // for it also means the cleanup below cannot race a late delivery.
            var denial = await WaitForDenialAsync(Guid.Parse(staffId));

            Assert.Equal(Guid.Parse(staffId), denial.Actor);
            Assert.Equal(role, denial.Role);
            Assert.Equal("PublishCorrection", denial.Action);
            Assert.NotNull(denialEventId);
            Assert.Equal(denialEventId.Value, denial.SourceEventId);
        }
        finally
        {
            await ResetAuditEntriesAsync(auditBaseline);

            if (denialEventId is { } id)
            {
                await _postgres.ExecuteAsync(
                    $"DELETE FROM identity.outbox_deliveries WHERE message_id = '{id}'");
                await _postgres.ExecuteAsync($"DELETE FROM identity.outbox_messages WHERE id = '{id}'");
            }
        }
    }

    /// <summary>
    /// The audit entry commits with the correction or the correction does not happen. The recorder
    /// throws after the correction row, the journey event and the outbox row were written, so the
    /// shared request transaction has to roll all three back and leave the cancelled journey as it
    /// was.
    /// </summary>
    [Fact]
    public async Task A_failing_audit_step_leaves_no_correction_and_the_journey_unchanged()
    {
        using var publishing = NewFactory();
        var journeyId = await CreateTodayJourneyAsync(publishing, RailwayToday());
        var auditBaseline = await MaxAuditPositionAsync();

        Guid? cancellationId = null;

        try
        {
            using (var client = publishing.CreateApiClient())
            using (var cancelled = await PostCancellationAsync(client, journeyId))
            {
                Assert.Equal(HttpStatusCode.Created, cancelled.StatusCode);

                var cancellation = await cancelled.Content.ReadFromJsonAsync<ChangeItem>();
                Assert.NotNull(cancellation);
                cancellationId = cancellation.Id;
            }

            var stateBefore = await ReadLiveStateAsync(journeyId);

            using var failing = NewFactory(configureServices: services =>
            {
                services.RemoveAll<IAuditRecorder>();
                services.AddScoped<IAuditRecorder, ThrowingAuditRecorder>();
            });

            using (var client = failing.CreateApiClient())
            using (var response = await PostCorrectionAsync(client, cancellationId.Value))
            {
                Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            }

            Assert.Equal(0, await CountAsync(
                $"""
                SELECT count(*)
                FROM operations.status_changes
                WHERE journey_id = '{journeyId}' AND corrects_change_id IS NOT NULL
                """));
            Assert.Equal(1, await CountAsync(
                $"SELECT count(*) FROM operations.status_changes WHERE journey_id = '{journeyId}'"));
            Assert.Equal(stateBefore, await ReadLiveStateAsync(journeyId));
        }
        finally
        {
            await CleanUpJourneyAsync(journeyId);
            await ResetAuditEntriesAsync(auditBaseline);
        }
    }

    /// <summary>
    /// Two controllers correcting one change at the same time: the journey's row lock orders the
    /// plans, the second sees the first's correction, and exactly one request succeeds.
    /// </summary>
    [Fact]
    public async Task Two_simultaneous_corrections_of_one_change_leave_exactly_one_success()
    {
        using var factory = NewFactory();
        var journeyId = await CreateTodayJourneyAsync(factory, RailwayToday());
        var auditBaseline = await MaxAuditPositionAsync();

        try
        {
            using var client = factory.CreateApiClient();
            using var cancelled = await PostCancellationAsync(client, journeyId);
            var cancellation = await cancelled.Content.ReadFromJsonAsync<ChangeItem>();
            Assert.NotNull(cancellation);

            using var firstClient = factory.CreateApiClient();
            using var secondClient = factory.CreateApiClient();

            var responses = await Task.WhenAll(
                PostCorrectionAsync(firstClient, cancellation.Id),
                PostCorrectionAsync(secondClient, cancellation.Id));

            try
            {
                var statuses = responses
                    .Select(response => response.StatusCode)
                    .OrderBy(status => (int)status)
                    .ToArray();

                Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Conflict], statuses);
                Assert.Equal(1, await CountAsync(
                    $"""
                    SELECT count(*)
                    FROM operations.status_changes
                    WHERE journey_id = '{journeyId}' AND corrects_change_id = '{cancellation.Id}'
                    """));
            }
            finally
            {
                foreach (var response in responses)
                {
                    response.Dispose();
                }
            }
        }
        finally
        {
            await CleanUpJourneyAsync(journeyId);
            await ResetAuditEntriesAsync(auditBaseline);
        }
    }

    private SmartRailApiFactory NewFactory(
        TestNetwork? network = null,
        Action<IServiceCollection>? configureServices = null) =>
        new(
            _postgres.ConnectionString,
            configureServices: services =>
            {
                if (network is not null)
                {
                    services.RemoveAll<INetworkCatalog>();
                    services.AddSingleton<INetworkCatalog>(network.Catalog);
                }

                configureServices?.Invoke(services);
            });

    private static DateOnly RailwayToday() => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTime(
            DateTimeOffset.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Colombo")).DateTime);

    private static DateTimeOffset DepartAt(DateOnly serviceDate) =>
        new(serviceDate.ToDateTime(new TimeOnly(2, 0)), TimeSpan.Zero);

    private static string CorrectionRoute(Guid changeId) =>
        $"{ApiRoutes.BasePath}/operations/changes/{changeId}/corrections";

    private static string CancellationRoute(Guid journeyId) =>
        $"{ApiRoutes.BasePath}/operations/journeys/{journeyId}/cancellation";

    private static string DelayRoute(Guid journeyId) =>
        $"{ApiRoutes.BasePath}/operations/journeys/{journeyId}/delays";

    private static string PlatformChangesRoute(Guid journeyId) =>
        $"{ApiRoutes.BasePath}/operations/journeys/{journeyId}/platform-changes";

    private static string TerminationRoute(Guid journeyId) =>
        $"{ApiRoutes.BasePath}/operations/journeys/{journeyId}/termination";

    private static string DetailRoute(Guid journeyId) =>
        $"{ApiRoutes.BasePath}/operations/journeys/{journeyId}";

    /// <summary>Three calling stops, the shape the shared rules are checked against.</summary>
    private static IReadOnlyList<JourneyStopDto> CallingStops(DateOnly serviceDate)
    {
        var departAt = DepartAt(serviceDate);

        return
        [
            new JourneyStopDto(1, Guid.CreateVersion7(), true, null, departAt, Guid.CreateVersion7()),
            new JourneyStopDto(
                2,
                Guid.CreateVersion7(),
                true,
                departAt.AddMinutes(10),
                departAt.AddMinutes(11),
                Guid.CreateVersion7()),
            new JourneyStopDto(3, Guid.CreateVersion7(), true, departAt.AddMinutes(20), null, null),
        ];
    }

    private async Task<Guid> CreateTodayJourneyAsync(SmartRailApiFactory factory, DateOnly serviceDate) =>
        await CreateJourneyAsync(factory, serviceDate, CallingStops(serviceDate));

    /// <summary>Five calling stops, the shape the early-termination scenario uses.</summary>
    private static IReadOnlyList<JourneyStopDto> FiveCallingStops(DateOnly serviceDate)
    {
        var departAt = DepartAt(serviceDate);

        return
        [
            new JourneyStopDto(1, Guid.CreateVersion7(), true, null, departAt, Guid.CreateVersion7()),
            new JourneyStopDto(
                2,
                Guid.CreateVersion7(),
                true,
                departAt.AddMinutes(10),
                departAt.AddMinutes(11),
                Guid.CreateVersion7()),
            new JourneyStopDto(
                3,
                Guid.CreateVersion7(),
                true,
                departAt.AddMinutes(20),
                departAt.AddMinutes(21),
                Guid.CreateVersion7()),
            new JourneyStopDto(
                4,
                Guid.CreateVersion7(),
                true,
                departAt.AddMinutes(30),
                departAt.AddMinutes(31),
                Guid.CreateVersion7()),
            new JourneyStopDto(5, Guid.CreateVersion7(), true, departAt.AddMinutes(40), null, null),
        ];
    }

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

    /// <summary>
    /// Records departures for the given stops and marks the journey running. The departure log table
    /// arrives with add-departure-logging, so the stored state is set directly, as the termination
    /// tests do.
    /// </summary>
    private async Task MakeRunningAsync(Guid journeyId, DateOnly serviceDate, params int[] departedStops)
    {
        foreach (var sequence in departedStops)
        {
            var departedAt = DepartAt(serviceDate).AddMinutes((sequence - 1) * 10);

            await _postgres.ExecuteAsync(
                $"""
                UPDATE operations.journey_stop_live_states
                SET actual_departure = '{departedAt:O}'
                WHERE journey_id = '{journeyId}' AND sequence = {sequence}
                """);
        }

        await _postgres.ExecuteAsync(
            $"UPDATE operations.journey_live_states SET lifecycle_status = 'running' WHERE journey_id = '{journeyId}'");
    }

    private static async Task StartAsync(SmartRailApiFactory factory)
    {
        using var client = factory.CreateApiClient();
        using var response = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> PostCorrectionAsync(
        HttpClient client,
        Guid changeId,
        object? body = null,
        string? token = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, CorrectionRoute(changeId));

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        request.WithStaffToken(token ?? TestTokens.Create("controller.mahesh", Roles.NetworkController));

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostCancellationAsync(
        HttpClient client,
        Guid journeyId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, CancellationRoute(journeyId));
        request.WithStaffToken(TestTokens.Create("controller.mahesh", Roles.NetworkController));

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostDelayAsync(
        HttpClient client,
        Guid journeyId,
        int fromStopSequence,
        int minutes)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, DelayRoute(journeyId))
        {
            Content = JsonContent.Create(new { fromStopSequence, minutes }),
        };

        request.WithStaffToken(TestTokens.Create("controller.mahesh", Roles.NetworkController));

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostPlatformChangeAsync(
        HttpClient client,
        Guid journeyId,
        int stopSequence,
        Guid platformId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, PlatformChangesRoute(journeyId))
        {
            Content = JsonContent.Create(new { stopSequence, platformId }),
        };

        request.WithStaffToken(TestTokens.Create("controller.mahesh", Roles.NetworkController));

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostTerminationAsync(
        HttpClient client,
        Guid journeyId,
        int lastStopSequence)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TerminationRoute(journeyId))
        {
            Content = JsonContent.Create(new { lastStopSequence }),
        };

        request.WithStaffToken(TestTokens.Create("controller.mahesh", Roles.NetworkController));

        return await client.SendAsync(request);
    }

    private static async Task<JourneyDetail> GetDetailAsync(HttpClient client, Guid journeyId)
    {
        using var response = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, DetailRoute(journeyId))
                .WithStaffToken(TestTokens.Create("controller.mahesh", Roles.NetworkController)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var detail = await response.Content.ReadFromJsonAsync<JourneyDetail>();
        Assert.NotNull(detail);
        return detail;
    }

    /// <summary>The journey's events in sequence order, as the channels read them.</summary>
    private static async Task<IReadOnlyList<JourneyEventView>> ReadEventsAsync(
        SmartRailApiFactory factory,
        Guid journeyId)
    {
        using var scope = factory.Services.CreateScope();

        return await scope.ServiceProvider
            .GetRequiredService<ILiveJourneys>()
            .GetEventsAfterAsync(0, journeyId);
    }

    private async Task<int> CountAsync(string sql)
    {
        var count = Assert.Single(await _postgres.QueryAsync(sql, reader => reader.GetInt64(0)));
        return (int)count;
    }

    /// <summary>The audit entry a change produced (package decision D-8).</summary>
    private async Task<Guid?> ReadAuditEntryIdAsync(Guid changeId)
    {
        var ids = await _postgres.QueryAsync(
            $"SELECT audit_entry_id FROM operations.status_changes WHERE id = '{changeId}'",
            reader => reader.IsDBNull(0) ? (Guid?)null : reader.GetGuid(0));

        return Assert.Single(ids);
    }

    /// <summary>The journey's stored state, so a failed correction can be shown to have left it alone.</summary>
    private async Task<string> ReadLiveStateAsync(Guid journeyId)
    {
        var stops = await _postgres.QueryAsync(
            $"""
            SELECT sequence, expected_arrival, expected_departure, actual_departure, platform_id
            FROM operations.journey_stop_live_states
            WHERE journey_id = '{journeyId}'
            ORDER BY sequence
            """,
            reader => string.Join(
                '|',
                reader.GetInt32(0),
                reader.IsDBNull(1) ? "-" : reader.GetFieldValue<DateTimeOffset>(1).ToString("O"),
                reader.IsDBNull(2) ? "-" : reader.GetFieldValue<DateTimeOffset>(2).ToString("O"),
                reader.IsDBNull(3) ? "-" : reader.GetFieldValue<DateTimeOffset>(3).ToString("O"),
                reader.IsDBNull(4) ? "-" : reader.GetGuid(4).ToString()));

        var state = Assert.Single(await _postgres.QueryAsync(
            $"""
            SELECT lifecycle_status, delay_minutes, last_departed_station_id, next_station_id,
                   last_sequence_number
            FROM operations.journey_live_states
            WHERE journey_id = '{journeyId}'
            """,
            reader => string.Join(
                '|',
                reader.GetString(0),
                reader.IsDBNull(1) ? "-" : reader.GetInt32(1).ToString(),
                reader.IsDBNull(2) ? "-" : reader.GetGuid(2).ToString(),
                reader.IsDBNull(3) ? "-" : reader.GetGuid(3).ToString(),
                reader.GetInt64(4))));

        return $"{state}::{string.Join(';', stops)}";
    }

    private async Task<long> MaxAuditPositionAsync() =>
        Assert.Single(await _postgres.QueryAsync(
            "SELECT coalesce(max(chain_position), 0) FROM audit.entries",
            reader => reader.GetInt64(0)));

    private async Task<long> MaxIdentityOutboxPositionAsync() =>
        Assert.Single(await _postgres.QueryAsync(
            "SELECT coalesce(max(position), 0) FROM identity.outbox_messages",
            reader => reader.GetInt64(0)));

    /// <summary>The denial message this request published, so the test can remove it again.</summary>
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
    /// Waits for a refused write to reach the audit record. Delivery is asynchronous, so the entry
    /// appears shortly after the 403 (audit design D5).
    /// </summary>
    private async Task<DenialRow> WaitForDenialAsync(Guid staffId)
    {
        var deadline = DateTimeOffset.UtcNow.Add(Timeout);

        while (true)
        {
            var rows = await _postgres.QueryAsync(
                $"""
                SELECT actor_staff_id, actor_role, action, source_event_id
                FROM audit.entries
                WHERE actor_staff_id = '{staffId}'
                  AND outcome = 'denied'
                  AND action = 'PublishCorrection'
                ORDER BY chain_position DESC
                LIMIT 1
                """,
                reader => new DenialRow(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetGuid(3)));

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
    /// Removes every row the test created for the journey: the corrections first (self reference),
    /// then the changes, the event, its outbox row, the stops and the state.
    /// </summary>
    private async Task CleanUpJourneyAsync(Guid journeyId)
    {
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

    private sealed record AuditValues(string? Before, string? After);

    private sealed record AuditRow(
        long ChainPosition,
        string? Before,
        string? After,
        string PreviousHash,
        string EntryHash);

    private sealed record DenialRow(Guid Actor, string Role, string Action, Guid SourceEventId);

    private sealed record PlatformItem(Guid Id, string Number);

    private sealed record StopItem(
        int Sequence,
        bool IsCalling,
        DateTimeOffset? ScheduledArrival,
        DateTimeOffset? ScheduledDeparture,
        DateTimeOffset? ExpectedArrival,
        DateTimeOffset? ExpectedDeparture,
        PlatformItem? Platform);

    private sealed record JourneyItem(string Status);

    private sealed record JourneyDetail(
        JourneyItem Journey,
        IReadOnlyList<StopItem> Stops,
        IReadOnlyList<ChangeItem> Changes);

    private sealed record ChangeItem(
        Guid Id,
        string Type,
        IReadOnlyDictionary<string, string?> Values,
        string? PublisherName,
        DateTimeOffset PublishedAt,
        string Provenance,
        bool Corrected);

    /// <summary>
    /// The network the platform test runs against: Beta has three platforms, so A and B can move the
    /// stop to two platforms that are not its starting one.
    /// </summary>
    private sealed class TestNetwork
    {
        public TestNetwork()
        {
            Catalog = new FakeNetworkCatalog(
            [
                new StationDto(
                    Alpha,
                    "ALP",
                    "Alpha",
                    IsInterchange: false,
                    [new PlatformDto(AlphaOne, Alpha, "1"), new PlatformDto(AlphaTwo, Alpha, "2")],
                    []),
                new StationDto(
                    Beta,
                    "BET",
                    "Beta",
                    IsInterchange: false,
                    [
                        new PlatformDto(BetaOne, Beta, "1"),
                        new PlatformDto(BetaTwo, Beta, "2"),
                        new PlatformDto(BetaThree, Beta, "3"),
                    ],
                    []),
                new StationDto(Gamma, "GAM", "Gamma", IsInterchange: false, [], []),
            ]);
        }

        public Guid Alpha { get; } = Guid.CreateVersion7();

        public Guid AlphaOne { get; } = Guid.CreateVersion7();

        public Guid AlphaTwo { get; } = Guid.CreateVersion7();

        public Guid Beta { get; } = Guid.CreateVersion7();

        public Guid BetaOne { get; } = Guid.CreateVersion7();

        public Guid BetaTwo { get; } = Guid.CreateVersion7();

        public Guid BetaThree { get; } = Guid.CreateVersion7();

        public Guid Gamma { get; } = Guid.CreateVersion7();

        public FakeNetworkCatalog Catalog { get; }

        /// <summary>Three calling stops; stop 2 is at Beta on platform "1".</summary>
        public IReadOnlyList<JourneyStopDto> Stops(DateOnly serviceDate)
        {
            var departAt = DepartAt(serviceDate);

            return
            [
                new JourneyStopDto(1, Alpha, true, null, departAt, AlphaOne),
                new JourneyStopDto(2, Beta, true, departAt.AddMinutes(10), departAt.AddMinutes(11), BetaOne),
                new JourneyStopDto(3, Gamma, true, departAt.AddMinutes(20), null, null),
            ];
        }
    }

    /// <summary>The station catalogue, substituted because the Rail Network holds no seed yet.</summary>
    private sealed class FakeNetworkCatalog(IReadOnlyList<StationDto> stations) : INetworkCatalog
    {
        public Task<IReadOnlyList<LineDto>> GetLinesAsync(
            ServiceDirection direction = ServiceDirection.Outbound,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LineDto>>([]);

        public Task<IReadOnlyList<StationDto>> GetStationsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(stations);

        public Task<StationDto?> GetStationAsync(Guid stationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(stations.FirstOrDefault(station => station.Id == stationId));

        public Task<IReadOnlyList<PlatformDto>> GetPlatformsAsync(
            Guid stationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PlatformDto>>(
                stations.FirstOrDefault(station => station.Id == stationId)?.Platforms ?? []);

        public Task<bool> IsInterchangeAsync(Guid stationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    /// <summary>An audit recorder that fails at the last step of the publishing pipeline.</summary>
    private sealed class ThrowingAuditRecorder : IAuditRecorder
    {
        public Task<Guid> RecordAsync(
            AuditRecordRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Injected audit failure after the correction was written.");
    }
}
