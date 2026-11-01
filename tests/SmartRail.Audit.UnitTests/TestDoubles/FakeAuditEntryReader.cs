using SmartRail.Audit.Application.Entries;
using SmartRail.Audit.Contracts;
using SmartRail.SharedKernel.Paging;

namespace SmartRail.Audit.UnitTests.TestDoubles;

/// <summary>
/// An in-memory audit read side. It filters and pages the entries it was given the way the real
/// reader does — newest first, with a missing filter value meaning no filter — and records the
/// filter and page each call received, so the handler's mapping can be checked without a database.
/// </summary>
internal sealed class FakeAuditEntryReader : IAuditEntryReader
{
    private readonly List<AuditEntryDetail> _entries;

    internal FakeAuditEntryReader(params AuditEntryDetail[] entries) => _entries = [.. entries];

    /// <summary>The filter the handler passed, so a test can check how it read the query.</summary>
    internal AuditEntryFilter? LastFilter { get; private set; }

    /// <summary>The page the handler passed, so a test can check the paging it asked for.</summary>
    internal PageRequest? LastPage { get; private set; }

    public Task<PagedResult<AuditEntryListItem>> GetEntriesAsync(
        AuditEntryFilter filter,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        LastFilter = filter;
        LastPage = page;

        var matching = _entries
            .Where(entry => Matches(entry, filter))
            .OrderByDescending(entry => entry.ChainPosition)
            .ToList();

        IReadOnlyList<AuditEntryListItem> items =
            [.. matching.Skip(page.Skip).Take(page.PageSize).Select(ToListItem)];

        return Task.FromResult(
            new PagedResult<AuditEntryListItem>(items, page.Page, page.PageSize, matching.Count));
    }

    public Task<AuditEntryDetail?> GetEntryAsync(
        Guid entryId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_entries.SingleOrDefault(entry => entry.Id == entryId));

    private static bool Matches(AuditEntryDetail entry, AuditEntryFilter filter) =>
        (filter.ActorStaffId is not { } actor || entry.ActorStaffId == actor)
        && (filter.From is not { } from || entry.OccurredAt >= from)
        && (filter.To is not { } to || entry.OccurredAt <= to)
        && (filter.Outcome is not { } outcome
            || string.Equals(entry.Outcome, outcome.ToString(), StringComparison.OrdinalIgnoreCase));

    private static AuditEntryListItem ToListItem(AuditEntryDetail entry) =>
        new(
            entry.Id,
            entry.ChainPosition,
            entry.OccurredAt,
            entry.ActorStaffId,
            entry.ActorRole,
            entry.Action,
            entry.Outcome,
            entry.TargetType,
            entry.TargetId);
}
