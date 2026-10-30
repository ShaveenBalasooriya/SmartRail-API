using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartRail.Audit.Application.Entries;
using SmartRail.Audit.Contracts;
using SmartRail.Audit.Domain.Entries;
using SmartRail.Audit.Infrastructure.Persistence;
using SmartRail.SharedKernel.Paging;

namespace SmartRail.Audit.Infrastructure.Repositories;

/// <summary>
/// Reads the audit record from the module's own schema. Entities are loaded untracked and projected
/// in memory, so no domain entity leaves the module.
/// </summary>
internal sealed class AuditEntryReader(AuditDbContext database) : IAuditEntryReader
{
    /// <inheritdoc />
    public async Task<PagedResult<AuditEntryListItem>> GetEntriesAsync(
        AuditEntryFilter filter,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = database.Entries.AsNoTracking();

        // A denial's actor may be unknown, so a missing actor is "no filter", never "entries with
        // no actor" (audit-record spec, Auditor list with filters).
        if (filter.ActorStaffId is { } actorStaffId)
        {
            query = query.Where(entry => entry.ActorStaffId == actorStaffId);
        }

        if (filter.From is { } from)
        {
            query = query.Where(entry => entry.OccurredAt >= from);
        }

        if (filter.To is { } to)
        {
            query = query.Where(entry => entry.OccurredAt <= to);
        }

        if (filter.Outcome is { } outcome)
        {
            query = query.Where(entry => entry.Outcome == outcome);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        // Newest first means the highest chain position first: the chain is the record's own order.
        var entries = await query
            .OrderByDescending(entry => entry.ChainPosition)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<AuditEntryListItem>(
            entries.Select(ToListItem).ToList(),
            page.Page,
            page.PageSize,
            totalCount);
    }

    /// <inheritdoc />
    public async Task<AuditEntryDetail?> GetEntryAsync(
        Guid entryId,
        CancellationToken cancellationToken = default)
    {
        var entry = await database.Entries
            .AsNoTracking()
            .SingleOrDefaultAsync(entry => entry.Id == entryId, cancellationToken);

        return entry is null ? null : ToDetail(entry);
    }

    private static AuditEntryListItem ToListItem(AuditEntry entry) => new(
        entry.Id,
        entry.ChainPosition,
        entry.OccurredAt,
        entry.ActorStaffId,
        entry.ActorRole,
        entry.Action,
        OutcomeName(entry.Outcome),
        entry.TargetType,
        entry.TargetId);

    private static AuditEntryDetail ToDetail(AuditEntry entry) => new(
        entry.Id,
        entry.ChainPosition,
        entry.OccurredAt,
        entry.ActorStaffId,
        entry.ActorRole,
        entry.Action,
        OutcomeName(entry.Outcome),
        entry.TargetType,
        entry.TargetId,
        ParseJson(entry.Before),
        ParseJson(entry.After),
        entry.RelatedEntryId,
        entry.PreviousHash,
        entry.EntryHash);

    /// <summary>The stored name, which is also the value the record's readers expect.</summary>
    private static string OutcomeName(AuditOutcome outcome) => outcome switch
    {
        AuditOutcome.Succeeded => "succeeded",
        AuditOutcome.Denied => "denied",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown audit outcome."),
    };

    /// <summary>
    /// Turns the stored jsonb text into an element the response writes as real JSON rather than as
    /// a string of JSON. The clone outlives the document it was read from.
    /// </summary>
    private static JsonElement? ParseJson(string? json)
    {
        if (json is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);

        return document.RootElement.Clone();
    }
}
