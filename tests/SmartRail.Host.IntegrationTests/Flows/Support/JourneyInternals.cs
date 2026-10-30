using SmartRail.Host.IntegrationTests.Infrastructure;

namespace SmartRail.Host.IntegrationTests.Flows.Support;

/// <summary>One departure log as the journey's stored record holds it.</summary>
/// <param name="StopSequence">The stop that departed.</param>
/// <param name="DepartedAt">The device time of the tap, as the Guard's queue sent it.</param>
/// <param name="ReceivedAt">The server time the log arrived.</param>
internal sealed record DepartureRow(int StopSequence, DateTimeOffset DepartedAt, DateTimeOffset ReceivedAt);

/// <summary>
/// Everything a refused write must leave untouched, read straight from the database because the API
/// has no reason to expose it: the journey's published changes, its journey events (the live stream's
/// record) and its outbox rows (what the channels are told). The three are rendered as text so a
/// before-and-after comparison prints exactly which row differs.
/// </summary>
/// <param name="StatusChanges">The journey's published changes, in publish order.</param>
/// <param name="JourneyEvents">The journey's events, in sequence order.</param>
/// <param name="OutboxMessages">The announcements queued for the journey.</param>
internal sealed record JourneyStateSnapshot(
    IReadOnlyList<string> StatusChanges,
    IReadOnlyList<string> JourneyEvents,
    IReadOnlyList<string> OutboxMessages)
{
    /// <summary>The whole snapshot as one comparable text.</summary>
    public string Render() =>
        $"{Environment.NewLine}status changes: {Rows(StatusChanges)}"
        + $"{Environment.NewLine}journey events: {Rows(JourneyEvents)}"
        + $"{Environment.NewLine}outbox rows: {Rows(OutboxMessages)}";

    private static string Rows(IReadOnlyList<string> rows) =>
        rows.Count == 0 ? "(none)" : "[" + string.Join(" ; ", rows) + "]";
}

/// <summary>
/// The white-box side of a flow: the journey's stored state, and the removal of everything the flow
/// created. Queries are scoped to the journey the flow owns, because the database is shared and its
/// other rows belong to other tests.
/// </summary>
internal sealed class JourneyInternals
{
    private readonly PostgresFixture _postgres;

    public JourneyInternals(PostgresFixture postgres) => _postgres = postgres;

    /// <summary>Reads the journey's changes, events and outbox rows as one comparable snapshot.</summary>
    public async Task<JourneyStateSnapshot> SnapshotAsync(Guid journeyId)
    {
        var changes = await _postgres.QueryAsync(
            $"""
            SELECT concat_ws('|', id, change_type, stop_sequence, delay_minutes, published_by,
                             published_at, fact_order, corrects_change_id)
            FROM operations.status_changes
            WHERE journey_id = '{journeyId}'
            ORDER BY fact_order
            """,
            reader => reader.GetString(0));

        var events = await _postgres.QueryAsync(
            $"""
            SELECT concat_ws('|', event_type, sequence_number, stream_position, status_change_id,
                             departure_log_id, occurred_at, recorded_at)
            FROM operations.journey_events
            WHERE journey_id = '{journeyId}'
            ORDER BY sequence_number
            """,
            reader => reader.GetString(0));

        // The outbox is read without its publish timestamp: that column is the dispatcher's own
        // bookkeeping and legitimately fills in while the flow runs, so it is not part of "what the
        // channels are told". The rows, their order and their payload are.
        var outbox = await _postgres.QueryAsync(
            $"""
            SELECT concat_ws('|', id, event_type, ordering_key, position, payload)
            FROM operations.outbox_messages
            WHERE ordering_key = '{journeyId}'
            ORDER BY position
            """,
            reader => reader.GetString(0));

        return new JourneyStateSnapshot(changes, events, outbox);
    }

    /// <summary>
    /// The departure logs the journey holds, in stop order: what the Guard recorded, with the device
    /// time of the tap kept apart from the server's receipt time.
    /// </summary>
    public async Task<IReadOnlyList<DepartureRow>> DeparturesAsync(Guid journeyId) =>
        await _postgres.QueryAsync(
            $"""
            SELECT stop_sequence, departed_at, received_at
            FROM operations.departure_logs
            WHERE journey_id = '{journeyId}'
            ORDER BY stop_sequence
            """,
            reader => new DepartureRow(
                reader.GetInt32(0),
                reader.GetFieldValue<DateTimeOffset>(1),
                reader.GetFieldValue<DateTimeOffset>(2)));

    /// <summary>The departure the live state records for one stop, or null while the stop has none.</summary>
    public async Task<DateTimeOffset?> ActualDepartureAsync(Guid journeyId, int stopSequence)
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

    /// <summary>The journey's stored lifecycle status, for a rule that closes a journey.</summary>
    public async Task<string> LifecycleStatusAsync(Guid journeyId)
    {
        var statuses = await _postgres.QueryAsync(
            $"SELECT lifecycle_status FROM operations.journey_live_states WHERE journey_id = '{journeyId}'",
            reader => reader.GetString(0));

        return Assert.Single(statuses);
    }

    /// <summary>
    /// Puts a journey beyond today's changes by marking it completed. Used only where a route's rule
    /// is about a completed journey and no endpoint completes one on demand.
    /// </summary>
    public Task MarkCompletedAsync(Guid journeyId) =>
        _postgres.ExecuteAsync(
            $"UPDATE operations.journey_live_states SET lifecycle_status = 'completed' WHERE journey_id = '{journeyId}'");

    /// <summary>The journey's highest stored stream position, which is the newest event on the wire.</summary>
    public async Task<long> LatestStreamPositionAsync(Guid journeyId) =>
        Assert.Single(await _postgres.QueryAsync(
            $"SELECT coalesce(max(stream_position), 0) FROM operations.journey_events WHERE journey_id = '{journeyId}'",
            reader => reader.GetInt64(0)));

    /// <summary>
    /// Waits until every announcement the journey queued has been dispatched and delivered to every
    /// consumer, so a flow that opens a stream next cannot receive a change it made while setting the
    /// journey up. Call it when the journey has at least one announcement, as after a departure.
    /// </summary>
    public async Task WaitForAnnouncementsDeliveredAsync(Guid journeyId, string behaviour)
    {
        await Eventually.ValueAsync(
            () => UndeliveredAnnouncementsAsync(journeyId),
            undelivered => undelivered == 0,
            behaviour);
    }

    /// <summary>
    /// The journey's announcements still to deliver. A message counts only once the dispatcher has
    /// recorded a delivery row for it, so a message the dispatcher has not touched yet is still
    /// outstanding rather than silently finished.
    /// </summary>
    private async Task<int> UndeliveredAnnouncementsAsync(Guid journeyId)
    {
        var rows = await _postgres.QueryAsync(
            $"""
            SELECT count(*) AS messages,
                   count(*) FILTER (WHERE d_total > 0 AND d_delivered = d_total) AS complete
            FROM (
                SELECT m.id,
                       count(d.consumer) AS d_total,
                       count(*) FILTER (WHERE d.status = 'delivered') AS d_delivered
                FROM operations.outbox_messages m
                LEFT JOIN operations.outbox_deliveries d ON d.message_id = m.id
                WHERE m.ordering_key = '{journeyId}'
                GROUP BY m.id
            ) per_message
            """,
            reader => (Messages: reader.GetInt64(0), Complete: reader.GetInt64(1)));

        var row = Assert.Single(rows);

        // Nothing queued yet is outstanding too: the caller is waiting for an announcement it saw the
        // request accept, so the dispatcher simply has not recorded it yet.
        return row.Messages == 0 ? 1 : (int)(row.Messages - row.Complete);
    }

    /// <summary>
    /// Removes every row the flow's journey created: the departure logs and changes first because the
    /// change table refers to itself, then the outbox with its deliveries, the events, the stops and the
    /// live state, and finally the generation announcement in the network module's outbox.
    /// </summary>
    public async Task CleanUpAsync(Guid journeyId)
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

        // The delivery rows refer to the message by identifier alone, so they go first: a pending row
        // whose message had been deleted would stall the module's outbox.
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
            $"""
            DELETE FROM network.outbox_deliveries
            WHERE message_id IN (
                SELECT id FROM network.outbox_messages
                WHERE id = '{journeyId}' OR ordering_key = '{journeyId}')
            """);

        await _postgres.ExecuteAsync(
            $"""
            DELETE FROM network.outbox_messages
            WHERE id = '{journeyId}' OR ordering_key = '{journeyId}'
            """);
    }
}
