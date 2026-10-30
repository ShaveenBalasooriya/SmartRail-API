using SmartRail.Host.IntegrationTests.Flows.Support;
using SmartRail.Host.IntegrationTests.Infrastructure;

namespace SmartRail.Host.IntegrationTests.Flows.Drivers;

/// <summary>One denial as the audit record stores it.</summary>
/// <param name="Id">The entry's identifier.</param>
/// <param name="ChainPosition">The entry's position in the append-only chain.</param>
/// <param name="OccurredAt">The time of the refused attempt, as the server recorded it.</param>
/// <param name="ActorStaffId">The staff member whose attempt was refused.</param>
/// <param name="ActorRole">The role that attempt carried.</param>
/// <param name="Action">The action that was attempted.</param>
/// <param name="Outcome">The outcome the record stored.</param>
/// <param name="SourceEventId">The refusal announcement this entry was written from.</param>
internal sealed record DenialEntry(
    Guid Id,
    long ChainPosition,
    DateTimeOffset OccurredAt,
    Guid? ActorStaffId,
    string? ActorRole,
    string Action,
    string Outcome,
    Guid SourceEventId);

/// <summary>One entry of the audit record as a flow reads it.</summary>
/// <param name="Id">The entry's identifier.</param>
/// <param name="ChainPosition">The entry's position in the append-only chain.</param>
/// <param name="OccurredAt">When the action happened.</param>
/// <param name="Action">The action the entry records.</param>
/// <param name="Outcome">Whether the action succeeded or was denied.</param>
/// <param name="ActorRole">The role the action was taken in.</param>
/// <param name="RelatedEntryId">The entry this one links to, as a correction links to the change it withdraws.</param>
internal sealed record AuditRecord(
    Guid Id,
    long ChainPosition,
    DateTimeOffset OccurredAt,
    string Action,
    string Outcome,
    string? ActorRole,
    Guid? RelatedEntryId);

/// <summary>
/// The audit record itself, read through the database rather than the Auditor's list. A denial is
/// written from an <c>AccessDenied</c> announcement by the bus, so the only way to prove that a
/// particular refusal produced exactly one entry is to look the entry up by the announcement it came
/// from. The record is append-only and shared, so every query here is scoped to the flow's own event
/// or to the position it started at — never to a global count.
/// </summary>
internal sealed class AuditDriver
{
    private readonly PostgresFixture _postgres;

    public AuditDriver(PostgresFixture postgres) => _postgres = postgres;

    /// <summary>The chain's current highest position, which is where a flow's own window starts.</summary>
    public async Task<long> MaxChainPositionAsync() => Assert.Single(await _postgres.QueryAsync(
        "SELECT coalesce(max(chain_position), 0) FROM audit.entries",
        reader => reader.GetInt64(0)));

    /// <summary>
    /// Removes the entries a flow appended, leaving the chain it found intact. The table is
    /// append-only, so the trigger is disabled for the delete and enabled again straight after — the
    /// same way the neighbouring suites leave the shared record as they found it, because other tests
    /// identify a denial by its actor and action and would otherwise see this flow's older entry.
    /// </summary>
    public async Task ClearAfterAsync(long baselinePosition)
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

    /// <summary>
    /// Waits for the one entry written from <paramref name="sourceEventId"/> and fails, naming what was
    /// awaited and what the record holds, when it does not appear.
    /// </summary>
    public async Task<DenialEntry> WaitForDenialAsync(Guid sourceEventId, string behaviour)
    {
        var entries = await Eventually.ValueAsync(
            () => EntriesFromAsync(sourceEventId),
            found => found.Count == 1,
            $"{behaviour} should be recorded once, from its refusal announcement");

        return entries[0];
    }

    /// <summary>
    /// Watches the record for a short window and fails if a denied entry appears after
    /// <paramref name="baselinePosition"/>. For the reads and unauthenticated attempts the record must
    /// stay silent about.
    /// </summary>
    public Task NothingRecordedAfterAsync(long baselinePosition, string behaviour) =>
        Eventually.NothingAsync(
            async () => await CountDenialsAfterAsync(baselinePosition) > 0,
            behaviour);

    /// <summary>
    /// Waits for the change's own audit entry: the one entry a successful publish appends for this
    /// journey, found by the action the route names and the journey it targets.
    /// </summary>
    public async Task<AuditRecord> WaitForJourneyEntryAsync(
        Guid journeyId,
        string action,
        string behaviour)
    {
        var entries = await Eventually.ValueAsync(
            () => JourneyEntriesAsync(journeyId, action),
            found => found.Count == 1,
            behaviour);

        return entries[0];
    }

    /// <summary>Every entry the journey's own changes appended, in chain order.</summary>
    public Task<IReadOnlyList<AuditRecord>> JourneyEntriesAsync(Guid journeyId) =>
        JourneyEntriesAsync(journeyId, action: null);

    /// <summary>
    /// The entries that name this journey as their target, newest last, so a flow can read the
    /// change's entry and the correction's entry and the link between them.
    /// </summary>
    private async Task<IReadOnlyList<AuditRecord>> JourneyEntriesAsync(Guid journeyId, string? action)
    {
        var filter = action is null ? string.Empty : $"AND action = '{action}'";

        return await _postgres.QueryAsync(
            $"""
            SELECT id, chain_position, occurred_at, action, outcome, actor_role, related_entry_id
            FROM audit.entries
            WHERE target_type = 'journey' AND target_id = '{journeyId}' {filter}
            ORDER BY chain_position
            """,
            reader => new AuditRecord(
                reader.GetGuid(0),
                reader.GetInt64(1),
                reader.GetFieldValue<DateTimeOffset>(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetGuid(6)));
    }

    /// <summary>
    /// The successful entries appended after the flow started, for the one-action audits where the
    /// flow asserts a delta from its own baseline rather than a global count.
    /// </summary>
    public async Task<IReadOnlyList<AuditRecord>> SucceededEntriesAfterAsync(long baselinePosition) =>
        await _postgres.QueryAsync(
            $"""
            SELECT id, chain_position, occurred_at, action, outcome, actor_role, related_entry_id
            FROM audit.entries
            WHERE chain_position > {baselinePosition} AND outcome = 'succeeded'
            ORDER BY chain_position
            """,
            reader => new AuditRecord(
                reader.GetGuid(0),
                reader.GetInt64(1),
                reader.GetFieldValue<DateTimeOffset>(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetGuid(6)));

    private async Task<IReadOnlyList<DenialEntry>> EntriesFromAsync(Guid sourceEventId) =>
        await _postgres.QueryAsync(
            $"""
            SELECT id, chain_position, occurred_at, actor_staff_id, actor_role, action, outcome,
                   source_event_id
            FROM audit.entries
            WHERE source_event_id = '{sourceEventId}'
            ORDER BY chain_position
            """,
            reader => new DenialEntry(
                reader.GetGuid(0),
                reader.GetInt64(1),
                reader.GetFieldValue<DateTimeOffset>(2),
                reader.IsDBNull(3) ? null : reader.GetGuid(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetGuid(7)));

    private async Task<long> CountDenialsAfterAsync(long baselinePosition) =>
        Assert.Single(await _postgres.QueryAsync(
            $"""
            SELECT count(*)
            FROM audit.entries
            WHERE chain_position > {baselinePosition} AND outcome = 'denied'
            """,
            reader => reader.GetInt64(0)));
}
