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
/// <param name="ActorStaffId">The staff member who acted.</param>
/// <param name="ActorRole">The role they acted in.</param>
/// <param name="Action">The action that happened.</param>
/// <param name="Outcome">Whether it succeeded or was denied.</param>
internal sealed record AuditEntryRow(
    Guid Id,
    long ChainPosition,
    DateTimeOffset OccurredAt,
    Guid? ActorStaffId,
    string? ActorRole,
    string Action,
    string Outcome);

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
/// The Auditor's read surface: the audit list and the chain integrity check. The Auditor's positive
/// control in the flow is that these reads are allowed — and, being reads, are never announced as a
/// denied attempt.
/// </summary>
internal sealed class AuditorDriver
{
    private readonly HttpClient _client;
    private readonly StaffSession _session;

    public AuditorDriver(HttpClient client, StaffSession session)
    {
        _client = client;
        _session = session;
    }

    /// <summary>Reads the audit list, newest first.</summary>
    public async Task<ApiAttempt<AuditPage>> ListAsync()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{ApiRoutes.BasePath}/audit/entries");
        request.WithStaffToken(_session.Token);

        using var response = await _client.SendAsync(request);

        return await ReadAsync<AuditPage>(response);
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
