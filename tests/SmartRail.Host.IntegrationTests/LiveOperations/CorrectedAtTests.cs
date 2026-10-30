using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using SmartRail.Host.Http;
using SmartRail.Host.IntegrationTests.Infrastructure;
using SmartRail.LiveOperations.Contracts;
using SmartRail.RailNetwork.Contracts;
using SmartRail.RailNetwork.Infrastructure.Persistence;
using SmartRail.SharedKernel.Application.Abstractions;
using SmartRail.SharedKernel.Application.Events;

namespace SmartRail.Host.IntegrationTests.LiveOperations;

/// <summary>
/// The correction time on the live state (package LD0, decision D-10): the publish time of the most
/// recent correction a journey received, kept in the state so the public view and the journey event
/// snapshot can both report it (passenger-departures spec "Platform changes and corrections visible";
/// disruption-guidance spec "Corrections replace guidance"). Corrections are published through the
/// real endpoints, so the whole write pipeline is what records the time.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CorrectedAtTests
{
    /// <summary>The seeded guard account (seed/identity/staff.json).</summary>
    private static readonly Guid GuardStaffId = Guid.Parse("01a15874-e69d-73b2-b24d-d85d22fdead2");

    private readonly PostgresFixture _postgres;

    public CorrectedAtTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task A_corrected_cancellation_reports_the_correction_time_on_the_journey_and_on_its_event()
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

            // A journey that has never been corrected carries no correction time.
            Assert.Null((await GetJourneyAsync(factory, journeyId)).CorrectedAt);

            using var corrected = await PostCorrectionAsync(client, cancellation.Id);
            Assert.Equal(HttpStatusCode.Created, corrected.StatusCode);

            var correction = await corrected.Content.ReadFromJsonAsync<ChangeItem>();
            Assert.NotNull(correction);

            // The corrected journey is its restored self and notes when it was corrected (PAX-12).
            var journey = await GetJourneyAsync(factory, journeyId);

            Assert.Equal(JourneyLifecycleStatus.Scheduled, journey.Status);
            Assert.Equal(Truncate(correction.PublishedAt), journey.CorrectedAt);

            // The events the stream will send say the same: the snapshot taken for the correction
            // carries the time, the one taken for the cancellation it withdrew does not.
            var events = await ReadEventsAsync(factory, journeyId);

            Assert.Equal(2, events.Count);

            var cancellationEvent = Assert.Single(
                events,
                journeyEvent => journeyEvent.Type == JourneyEventType.CancellationPublished);
            Assert.Null(cancellationEvent.Snapshot.CorrectedAt);

            var correctionEvent = Assert.Single(
                events,
                journeyEvent => journeyEvent.Type == JourneyEventType.CorrectionPublished);
            Assert.Equal(journey.CorrectedAt, correctionEvent.Snapshot.CorrectedAt);
        }
        finally
        {
            await CleanUpJourneyAsync(journeyId);
            await ResetAuditEntriesAsync(auditBaseline);
        }
    }

    [Fact]
    public async Task A_later_correction_moves_the_correction_time_forward()
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
            var firstCorrection = await first.Content.ReadFromJsonAsync<ChangeItem>();
            Assert.NotNull(firstCorrection);

            Assert.Equal(Truncate(firstCorrection.PublishedAt), (await GetJourneyAsync(factory, journeyId)).CorrectedAt);

            // Published times come from the system clock, so leave a gap the clock granularity cannot
            // swallow before the second correction is published.
            await Task.Delay(TimeSpan.FromMilliseconds(50));

            using var delayed = await PostDelayAsync(client, journeyId, fromStopSequence: 2, minutes: 5);
            Assert.Equal(HttpStatusCode.Created, delayed.StatusCode);

            var delay = await delayed.Content.ReadFromJsonAsync<ChangeItem>();
            Assert.NotNull(delay);

            using var second = await PostCorrectionAsync(client, delay.Id);
            Assert.Equal(HttpStatusCode.Created, second.StatusCode);

            var secondCorrection = await second.Content.ReadFromJsonAsync<ChangeItem>();
            Assert.NotNull(secondCorrection);

            // The time is the most recent correction's, not the first one's.
            var journey = await GetJourneyAsync(factory, journeyId);

            Assert.Equal(Truncate(secondCorrection.PublishedAt), journey.CorrectedAt);
            Assert.True(journey.CorrectedAt > Truncate(firstCorrection.PublishedAt));
        }
        finally
        {
            await CleanUpJourneyAsync(journeyId);
            await ResetAuditEntriesAsync(auditBaseline);
        }
    }

    /// <summary>
    /// Snapshots written before the property existed have no <c>correctedAt</c> at all. Reading one
    /// through the module's own event query has to give <see langword="null"/> rather than fail.
    /// </summary>
    [Fact]
    public async Task A_snapshot_stored_without_the_correction_time_still_deserialises()
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
            Assert.Equal(HttpStatusCode.Created, corrected.StatusCode);

            var correctionEvent = Assert.Single(
                await ReadEventsAsync(factory, journeyId),
                journeyEvent => journeyEvent.Type == JourneyEventType.CorrectionPublished);
            Assert.NotNull(correctionEvent.Snapshot.CorrectedAt);

            // The shape of an event recorded before the correction time was part of the snapshot.
            await _postgres.ExecuteAsync(
                $"""
                UPDATE operations.journey_events
                SET payload = payload - 'correctedAt'
                WHERE journey_id = '{journeyId}'
                """);

            var events = await ReadEventsAsync(factory, journeyId);

            Assert.Equal(2, events.Count);
            Assert.All(events, journeyEvent => Assert.Null(journeyEvent.Snapshot.CorrectedAt));
        }
        finally
        {
            await CleanUpJourneyAsync(journeyId);
            await ResetAuditEntriesAsync(auditBaseline);
        }
    }

    private SmartRailApiFactory NewFactory() => new(_postgres.ConnectionString);

    private static DateOnly RailwayToday() => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTime(
            DateTimeOffset.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Colombo")).DateTime);

    private static DateTimeOffset DepartAt(DateOnly serviceDate) =>
        new(serviceDate.ToDateTime(new TimeOnly(2, 0)), TimeSpan.Zero);

    /// <summary>The microseconds PostgreSQL keeps, which is the precision the live state stores.</summary>
    private static DateTimeOffset Truncate(DateTimeOffset value) => new(
        value.UtcTicks - (value.UtcTicks % TimeSpan.TicksPerMicrosecond),
        TimeSpan.Zero);

    private static string CorrectionRoute(Guid changeId) =>
        $"{ApiRoutes.BasePath}/operations/changes/{changeId}/corrections";

    private static string CancellationRoute(Guid journeyId) =>
        $"{ApiRoutes.BasePath}/operations/journeys/{journeyId}/cancellation";

    private static string DelayRoute(Guid journeyId) =>
        $"{ApiRoutes.BasePath}/operations/journeys/{journeyId}/delays";

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

    private static async Task StartAsync(SmartRailApiFactory factory)
    {
        using var client = factory.CreateApiClient();
        using var response = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>The journey as another module or the public API reads it.</summary>
    private static async Task<LiveJourneyView> GetJourneyAsync(
        SmartRailApiFactory factory,
        Guid journeyId)
    {
        using var scope = factory.Services.CreateScope();

        var journey = await scope.ServiceProvider
            .GetRequiredService<ILiveJourneys>()
            .GetAsync(journeyId);

        Assert.NotNull(journey);
        return journey;
    }

    /// <summary>The journey's events in sequence order, as the live stream reads them.</summary>
    private static async Task<IReadOnlyList<JourneyEventView>> ReadEventsAsync(
        SmartRailApiFactory factory,
        Guid journeyId)
    {
        using var scope = factory.Services.CreateScope();

        return await scope.ServiceProvider
            .GetRequiredService<ILiveJourneys>()
            .GetEventsAfterAsync(0, journeyId);
    }

    private static async Task<HttpResponseMessage> PostCorrectionAsync(HttpClient client, Guid changeId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, CorrectionRoute(changeId));
        request.WithStaffToken(TestTokens.Create("controller.mahesh", Roles.NetworkController));

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

    private async Task<int> CountAsync(string sql)
    {
        var count = Assert.Single(await _postgres.QueryAsync(sql, reader => reader.GetInt64(0)));
        return (int)count;
    }

    private async Task<long> MaxAuditPositionAsync() =>
        Assert.Single(await _postgres.QueryAsync(
            "SELECT coalesce(max(chain_position), 0) FROM audit.entries",
            reader => reader.GetInt64(0)));

    /// <summary>
    /// Removes every row the test created for the journey: the corrections first (self reference),
    /// then the changes, the outbox rows, the events, the stops and the state.
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

    private sealed record ChangeItem(Guid Id, DateTimeOffset PublishedAt);
}
