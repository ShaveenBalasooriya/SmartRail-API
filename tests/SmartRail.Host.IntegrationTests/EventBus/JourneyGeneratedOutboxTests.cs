using System.Net;
using SmartRail.Host.IntegrationTests.Infrastructure;

namespace SmartRail.Host.IntegrationTests.EventBus;

/// <summary>
/// The journey-generation half of moving <see cref="SmartRail.RailNetwork.Contracts.JourneyGenerated"/>
/// onto the event bus (event-publishing spec: recorded atomically with the change; event-delivery
/// spec: every consumer receives every event once). Generation runs at start-up, so this test
/// creates its own line, stations, platforms and service, then starts the real Host and checks that
/// every journey it generated has exactly one live state and one announcement, and that a second
/// start publishes nothing new.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class JourneyGeneratedOutboxTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Every_generated_journey_gets_one_live_state_and_a_second_start_publishes_nothing_new()
    {
        // The first start applies the network outbox migration; nothing is seeded, so it generates
        // no journeys. The test's own data is inserted after it, as generation runs at start-up.
        await StartAsync();

        var network = await SeedServiceAsync();

        try
        {
            await ExerciseAsync(network.ServiceId);
        }
        finally
        {
            // The database is shared by the whole collection, and other tests assert exact row
            // counts, so every row this test caused is removed whether it passed or failed.
            await CleanUpAsync(network);
        }
    }

    private async Task ExerciseAsync(Guid serviceId)
    {
        Guid[] journeyIds;

        using (var firstStart = new SmartRailApiFactory(postgres.ConnectionString))
        {
            using var client = firstStart.CreateApiClient();
            await ReadyAsync(client);

            journeyIds = await WaitForJourneysAsync(serviceId);
            Assert.Equal(2, journeyIds.Length);

            await WaitForLiveStatesAsync(journeyIds);

            // Each generated journey has exactly one live state and one recorded announcement; the
            // announcement carries the journey's ID, so regeneration cannot record a second one.
            Assert.Equal(journeyIds.Order().ToArray(), (await LiveStateJourneyIdsAsync(journeyIds)).Order().ToArray());
            Assert.Equal(journeyIds.Order().ToArray(), (await AnnouncedJourneyIdsAsync(journeyIds)).Order().ToArray());
        }

        var liveStateIds = await LiveStateJourneyIdsAsync(journeyIds);
        var announcedIds = await AnnouncedJourneyIdsAsync(journeyIds);

        using (var secondStart = new SmartRailApiFactory(postgres.ConnectionString))
        {
            using var client = secondStart.CreateApiClient();
            await ReadyAsync(client);

            // Generation runs once more at start-up and inserts nothing because the journeys exist.
            await Task.Delay(TimeSpan.FromSeconds(3));

            Assert.Equal(2, (await JourneysAsync(serviceId)).Length);
            Assert.Equal(announcedIds, await AnnouncedJourneyIdsAsync(journeyIds));
            Assert.Equal(liveStateIds, await LiveStateJourneyIdsAsync(journeyIds));
        }
    }

    private async Task StartAsync()
    {
        using var factory = new SmartRailApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        await ReadyAsync(client);
    }

    private static async Task ReadyAsync(HttpClient client)
    {
        var ready = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
    }

    /// <summary>
    /// Inserts one line with two stations and platforms, and one service stopping at both, so
    /// start-up generation creates a journey for today and one for tomorrow. Direct SQL keeps the
    /// test independent of the seed data the owner has not decided on.
    /// </summary>
    private async Task<SeededNetwork> SeedServiceAsync()
    {
        var network = new SeededNetwork(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7());

        var firstPlatformId = Guid.CreateVersion7();
        var secondPlatformId = Guid.CreateVersion7();
        var code = Guid.CreateVersion7().ToString("N")[..8].ToUpperInvariant();

        await postgres.ExecuteAsync(
            $"""
            INSERT INTO network.lines (id, code, color, name)
            VALUES ('{network.LineId}', 'L{code}', '#64748b', 'Test Line');

            INSERT INTO network.stations (id, code, name)
            VALUES
                ('{network.FirstStationId}', 'A{code}', 'Test Station A'),
                ('{network.SecondStationId}', 'B{code}', 'Test Station B');

            INSERT INTO network.platforms (id, station_id, number)
            VALUES
                ('{firstPlatformId}', '{network.FirstStationId}', '1'),
                ('{secondPlatformId}', '{network.SecondStationId}', '1');

            INSERT INTO network.line_stations (id, line_id, station_id, sequence)
            VALUES
                ('{Guid.CreateVersion7()}', '{network.LineId}', '{network.FirstStationId}', 1),
                ('{Guid.CreateVersion7()}', '{network.LineId}', '{network.SecondStationId}', 2);

            INSERT INTO network.services (id, line_id, direction, name, guard_staff_id)
            VALUES ('{network.ServiceId}', '{network.LineId}', 'outbound', 'Test Service', '{Guid.CreateVersion7()}');

            INSERT INTO network.service_stops
                (id, service_id, station_id, sequence, is_calling, scheduled_arrival,
                 scheduled_departure, platform_id)
            VALUES
                ('{Guid.CreateVersion7()}', '{network.ServiceId}', '{network.FirstStationId}', 1, true, NULL,
                 '08:00:00', '{firstPlatformId}'),
                ('{Guid.CreateVersion7()}', '{network.ServiceId}', '{network.SecondStationId}', 2, true, '08:10:00',
                 '08:11:00', '{secondPlatformId}');
            """);

        return network;
    }

    /// <summary>
    /// Deletes everything this test caused, children first: the live states, the deliveries and
    /// announcements of its journeys, the journeys themselves, then the timetable and network rows.
    /// Other tests' rows are matched by nothing here.
    /// </summary>
    private async Task CleanUpAsync(SeededNetwork network)
    {
        await postgres.ExecuteAsync(
            $"""
            DELETE FROM operations.journey_live_states
            WHERE journey_id IN (SELECT id FROM network.journeys WHERE service_id = '{network.ServiceId}');

            DELETE FROM network.outbox_deliveries
            WHERE message_id IN (
                SELECT id FROM network.outbox_messages
                WHERE event_type = 'JourneyGenerated'
                  AND id IN (SELECT id FROM network.journeys WHERE service_id = '{network.ServiceId}'));

            DELETE FROM network.outbox_messages
            WHERE event_type = 'JourneyGenerated'
              AND id IN (SELECT id FROM network.journeys WHERE service_id = '{network.ServiceId}');

            DELETE FROM network.journeys WHERE service_id = '{network.ServiceId}';

            DELETE FROM network.service_stops WHERE service_id = '{network.ServiceId}';

            DELETE FROM network.services WHERE id = '{network.ServiceId}';

            DELETE FROM network.line_stations WHERE line_id = '{network.LineId}';

            DELETE FROM network.platforms
            WHERE station_id IN ('{network.FirstStationId}', '{network.SecondStationId}');

            DELETE FROM network.stations
            WHERE id IN ('{network.FirstStationId}', '{network.SecondStationId}');

            DELETE FROM network.lines WHERE id = '{network.LineId}';
            """);
    }

    /// <summary>The rows this test inserts, kept so it can remove exactly them again.</summary>
    private sealed record SeededNetwork(
        Guid LineId,
        Guid FirstStationId,
        Guid SecondStationId,
        Guid ServiceId);

    /// <summary>Waits up to ten seconds for start-up generation to insert the service's two journeys.</summary>
    private async Task<Guid[]> WaitForJourneysAsync(Guid serviceId)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        var journeyIds = Array.Empty<Guid>();

        while (journeyIds.Length < 2 && DateTimeOffset.UtcNow < deadline)
        {
            journeyIds = await JourneysAsync(serviceId);
            if (journeyIds.Length < 2)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100));
            }
        }

        return journeyIds;
    }

    /// <summary>
    /// Waits up to eight seconds for the consumer to create every journey's live state. The
    /// start-up backfill waits ten seconds before it runs, so a state row here can only come from
    /// the <c>live-operations.journey-generated</c> consumer.
    /// </summary>
    private async Task WaitForLiveStatesAsync(Guid[] journeyIds)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(8);
        var liveStateIds = Array.Empty<Guid>();

        while (liveStateIds.Length < journeyIds.Length && DateTimeOffset.UtcNow < deadline)
        {
            liveStateIds = await LiveStateJourneyIdsAsync(journeyIds);
            if (liveStateIds.Length < journeyIds.Length)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100));
            }
        }
    }

    private async Task<Guid[]> JourneysAsync(Guid serviceId)
    {
        var ids = await postgres.QueryAsync(
            $"SELECT id FROM network.journeys WHERE service_id = '{serviceId}' ORDER BY id",
            reader => reader.GetGuid(0));

        return [.. ids];
    }

    private async Task<Guid[]> LiveStateJourneyIdsAsync(Guid[] journeyIds)
    {
        var ids = await postgres.QueryAsync(
            "SELECT journey_id FROM operations.journey_live_states "
            + $"WHERE {InClause("journey_id", journeyIds)} ORDER BY journey_id",
            reader => reader.GetGuid(0));

        return [.. ids];
    }

    private async Task<Guid[]> AnnouncedJourneyIdsAsync(Guid[] journeyIds)
    {
        var ids = await postgres.QueryAsync(
            "SELECT id FROM network.outbox_messages "
            + $"WHERE event_type = 'JourneyGenerated' AND {InClause("id", journeyIds)} ORDER BY id",
            reader => reader.GetGuid(0));

        return [.. ids];
    }

    private static string InClause(string column, Guid[] ids) =>
        $"{column} IN ({string.Join(", ", ids.Select(id => $"'{id}'"))})";
}
