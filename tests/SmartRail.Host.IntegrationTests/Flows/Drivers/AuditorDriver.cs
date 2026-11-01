using System.Net;
using System.Text.Json;
using SmartRail.Host.Http;
using SmartRail.Host.IntegrationTests.Flows.Support;
using SmartRail.Host.IntegrationTests.Infrastructure;

namespace SmartRail.Host.IntegrationTests.Flows.Drivers;

/// <summary>One entry of the Auditor's list, as the list returns it.</summary>
/// <param name="Id">The entry's identifier.</param>
/// <param name="ChainPosition">The entry's position in the single append-only chain.</param>
/// <param name="OccurredAt">When the action happened.</param>
/// <param name="ActorStaffId">The staff member who acted, when the record knows them.</param>
/// <param name="ActorRole">The role they acted in.</param>
/// <param name="Action">The action that happened.</param>
/// <param name="Outcome">Whether it succeeded or was denied.</param>
/// <param name="TargetType">The kind of thing the action changed; null for a denial.</param>
/// <param name="TargetId">The identifier of the thing the action changed; null for a denial.</param>
/// <param name="RelatedEntryId">The entry a correction withdraws, which only the entry's own view carries.</param>
internal sealed record AuditEntryRow(
    Guid Id,
    long ChainPosition,
    DateTimeOffset OccurredAt,
    Guid? ActorStaffId,
    string? ActorRole,
    string Action,
    string Outcome,
    string? TargetType,
    string? TargetId,
    Guid? RelatedEntryId)
{
    /// <summary>True when this entry is the one a change to <paramref name="journeyId"/> produced.</summary>
    public bool Targets(Guid journeyId) =>
        string.Equals(TargetType, "journey", StringComparison.Ordinal)
        && Guid.TryParse(TargetId, out var target)
        && target == journeyId;
}

/// <summary>One page of the Auditor's list.</summary>
/// <param name="Items">The entries, newest first.</param>
/// <param name="TotalCount">How many entries match the filter in total.</param>
internal sealed record AuditPage(IReadOnlyList<AuditEntryRow> Items, int TotalCount);

/// <summary>The chain integrity check's answer.</summary>
/// <param name="Status"><c>intact</c> when every entry verified, otherwise <c>broken</c>.</param>
/// <param name="FirstFailingPosition">The first position that failed, absent while the chain is intact.</param>
/// <param name="EntriesChecked">How many entries were examined.</param>
internal sealed record AuditIntegrity(string Status, long? FirstFailingPosition, long EntriesChecked);

/// <summary>
/// The Auditor's read surface — the audit list, one entry in full and the chain integrity check — which
/// is how a flow reads the record. Nothing here touches the database: the Auditor's own routes are the
/// public equivalent of the table, so the same assertions run against a deployed API.
/// </summary>
/// <remarks>
/// An append-only record is shared by every test, so a flow never asks for "the newest entry": it asks
/// for the entries that name its own journey, or for the entries a refused attempt added to a baseline
/// the flow took itself. The polling waits only for the event bus to deliver what the request already
/// committed.
/// </remarks>
internal sealed class AuditorDriver
{
    /// <summary>The largest page the audit list accepts (design D6).</summary>
    private const int PageSize = 100;

    private readonly HttpClient _client;
    private readonly StaffSession _session;

    public AuditorDriver(HttpClient client, StaffSession session)
    {
        _client = client;
        _session = session;
    }

    /// <summary>Reads the audit list, newest first, optionally narrowed by the list's own filters.</summary>
    public async Task<ApiAttempt<AuditPage>> ListAsync(
        Guid? actorId = null,
        string? outcome = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null)
    {
        var query = new List<string> { $"pageSize={PageSize}" };

        if (actorId is { } actor)
        {
            query.Add($"actorId={actor}");
        }

        if (outcome is not null)
        {
            query.Add($"outcome={Uri.EscapeDataString(outcome)}");
        }

        if (from is { } start)
        {
            query.Add($"from={Uri.EscapeDataString(start.ToString("O", System.Globalization.CultureInfo.InvariantCulture))}");
        }

        if (to is { } end)
        {
            query.Add($"to={Uri.EscapeDataString(end.ToString("O", System.Globalization.CultureInfo.InvariantCulture))}");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{ApiRoutes.BasePath}/audit/entries?{string.Join("&", query)}");
        request.WithStaffToken(_session.Token);

        using var response = await _client.SendAsync(request);

        return await ReadAsync<AuditPage>(response);
    }

    /// <summary>Reads one entry in full, including the entry a correction withdraws.</summary>
    public async Task<ApiAttempt<AuditEntryRow>> EntryAsync(Guid entryId)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{ApiRoutes.BasePath}/audit/entries/{entryId}");
        request.WithStaffToken(_session.Token);

        using var response = await _client.SendAsync(request);

        return await ReadAsync<AuditEntryRow>(response);
    }

    /// <summary>Asks the record to verify its own hash chain.</summary>
    public async Task<ApiAttempt<AuditIntegrity>> IntegrityAsync()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{ApiRoutes.BasePath}/audit/integrity");
        request.WithStaffToken(_session.Token);

        using var response = await _client.SendAsync(request);

        return await ReadAsync<AuditIntegrity>(response);
    }

    /// <summary>
    /// Waits for the one succeeded entry the flow's own change to <paramref name="journeyId"/> appended,
    /// found by the journey the entry names and the action the route records.
    /// </summary>
    public async Task<AuditEntryRow> WaitForJourneyEntryAsync(
        Guid journeyId,
        string action,
        string behaviour)
    {
        var entries = await Eventually.ValueAsync(
            () => JourneyEntriesAsync(journeyId),
            found => found.Count(entry => string.Equals(entry.Action, action, StringComparison.Ordinal)) == 1,
            behaviour);

        return entries.Single(entry => string.Equals(entry.Action, action, StringComparison.Ordinal));
    }

    /// <summary>Waits until the flow's journey has <paramref name="expected"/> entries, in chain order.</summary>
    public async Task<IReadOnlyList<AuditEntryRow>> WaitForJourneyEntriesAsync(
        Guid journeyId,
        int expected,
        string behaviour)
    {
        return await Eventually.ValueAsync(
            () => JourneyEntriesAsync(journeyId),
            found => found.Count == expected,
            behaviour);
    }

    /// <summary>Every entry that names the journey, oldest first, as one page of the list holds them.</summary>
    public async Task<IReadOnlyList<AuditEntryRow>> JourneyEntriesAsync(Guid journeyId)
    {
        var page = await ListAsync(outcome: "succeeded");
        Verify.Ok(page, "The Auditor should be able to read the audit list");

        return [.. page.Value!.Items.Where(entry => entry.Targets(journeyId)).Reverse()];
    }

    /// <summary>The identifiers of the denied entries recorded for one staff member.</summary>
    public async Task<IReadOnlySet<Guid>> DeniedEntryIdsAsync(Guid actorId)
    {
        var page = await ListAsync(actorId: actorId, outcome: "denied");
        Verify.Ok(page, "The Auditor should be able to read the denied entries of one actor");

        return page.Value!.Items.Select(entry => entry.Id).ToHashSet();
    }

    /// <summary>
    /// Waits for the one denied entry a refused write added to <paramref name="baseline"/>, so a flow can
    /// name the entry its own attempt produced without asking for the newest row.
    /// </summary>
    public async Task<AuditEntryRow> WaitForNewDenialAsync(
        Guid actorId,
        string action,
        IReadOnlySet<Guid> baseline,
        string behaviour)
    {
        var page = await Eventually.ValueAsync(
            async () => await DenialsAsync(actorId, baseline),
            entries => entries.Count > 0,
            behaviour);

        return page.Single(entry => string.Equals(entry.Action, action, StringComparison.Ordinal));
    }

    /// <summary>
    /// Watches the denied entries for a short window and fails if a record appears that
    /// <paramref name="baseline"/> did not hold. For the refusals and the reads the record must stay
    /// silent about.
    /// </summary>
    public Task NothingDeniedAfterAsync(Guid actorId, IReadOnlySet<Guid> baseline, string behaviour) =>
        Eventually.NothingAsync(
            async () => (await DenialsAsync(actorId, baseline)).Count > 0,
            behaviour);

    /// <summary>
    /// Watches the denied entries for a short window and fails if a second record appears beside the one
    /// a refused attempt already produced: the record must hold the denial once, not once per consumer.
    /// </summary>
    public Task OnlyOneDenialAppearsAsync(Guid actorId, IReadOnlySet<Guid> baseline, string behaviour) =>
        Eventually.NothingAsync(
            async () => (await DenialsAsync(actorId, baseline)).Count > 1,
            behaviour);

    private async Task<IReadOnlyList<AuditEntryRow>> DenialsAsync(
        Guid actorId,
        IReadOnlySet<Guid> baseline)
    {
        var page = await ListAsync(actorId: actorId, outcome: "denied");
        Verify.Ok(page, "The Auditor should be able to read the denied entries of one actor");

        return [.. page.Value!.Items.Where(entry => !baseline.Contains(entry.Id))];
    }

    private static async Task<ApiAttempt<T>> ReadAsync<T>(HttpResponseMessage response)
        where T : class
    {
        var text = await response.Content.ReadAsStringAsync();
        var mediaType = response.Content.Headers.ContentType?.MediaType;

        T? value = null;

        if (response.StatusCode == HttpStatusCode.OK && text.Length > 0)
        {
            value = JsonSerializer.Deserialize<T>(text, JsonSerializerOptions.Web);
        }

        return new ApiAttempt<T>(response.StatusCode, mediaType, text, value);
    }
}
