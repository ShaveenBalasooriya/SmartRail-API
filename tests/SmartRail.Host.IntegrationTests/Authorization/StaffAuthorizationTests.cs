using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SmartRail.Host.IntegrationTests.Infrastructure;
using SmartRail.Host.Http;
using SmartRail.IdentityAccess.Contracts;
using SmartRail.SharedKernel.Application.Abstractions;
using SmartRail.SharedKernel.Infrastructure;

namespace SmartRail.Host.IntegrationTests.Authorization;

/// <summary>
/// The access rules of the staff surface, against locally signed tokens (design D11): the role
/// check, the fallback for an unclassified endpoint, the public endpoints, and the denial event
/// that only a refused write publishes (role-based-access spec, event-publishing spec).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class StaffAuthorizationTests : IDisposable
{
    /// <summary>The seeded controller account (seed/identity/staff.json).</summary>
    private const string ControllerStaffId = "01a15874-e69c-70da-a689-76a7268ed330";

    /// <summary>The seeded guard account (seed/identity/staff.json).</summary>
    private const string GuardStaffId = "01a15874-e69d-73b2-b24d-d85d22fdead2";

    private const string IdentityMeRoute = $"{ApiRoutes.BasePath}/identity/me";

    private readonly AccessDeniedRecorder _denials = new();
    private readonly PostgresFixture _postgres;
    private readonly SmartRailApiFactory _factory;
    private readonly HttpClient _client;

    public StaffAuthorizationTests(PostgresFixture postgres)
    {
        _postgres = postgres;
        _factory = new SmartRailApiFactory(
            postgres.ConnectionString,
            configureServices: services =>
            {
                services.AddEventConsumer<AccessDenied, AccessDeniedRecorder>();

                // Registered last, so the bus resolves the instance this test holds rather than a
                // new scoped consumer it could not observe.
                services.AddSingleton(_denials);
            });
        _client = _factory.CreateApiClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task Guard_on_a_controller_only_write_is_forbidden_and_announced_once()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TestEndpoints.ControllerWriteRoute)
            .WithStaffToken(TestTokens.Create("guard.ishara", Roles.Guard));

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        // The outbox row is written with the refusal; the dispatcher delivers it shortly after.
        await WaitForDeliveryAsync();

        var denial = Assert.Single(_denials.Denied);
        Assert.Equal(Guid.Parse(GuardStaffId), denial.StaffId);
        Assert.Equal(Roles.Guard, denial.Role);
        Assert.Equal("TestControllerOnlyWrite", denial.Action);
        Assert.True(
            denial.AttemptedAt > DateTimeOffset.UtcNow.AddMinutes(-1),
            $"AttemptedAt {denial.AttemptedAt} is not the server's current time.");

        // One refusal leaves exactly one published outbox row in the module's own schema
        // (event-publishing spec: access denied).
        var outboxRows = await _postgres.QueryAsync(
            """
            SELECT payload, published_at
            FROM identity.outbox_messages
            WHERE event_type = 'AccessDenied'
            """,
            reader => (
                Payload: reader.GetString(0),
                PublishedAt: reader.IsDBNull(1) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(1)));

        var row = Assert.Single(outboxRows);
        Assert.NotNull(row.PublishedAt);
        Assert.Contains(GuardStaffId, row.Payload, StringComparison.Ordinal);
        Assert.Contains("TestControllerOnlyWrite", row.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Guard_on_a_controller_only_read_is_forbidden_and_not_announced()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, TestEndpoints.ControllerReadRoute)
            .WithStaffToken(TestTokens.Create("guard.ishara", Roles.Guard));

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // Long enough for the dispatcher's one-second poll: a refusal that was recorded would have
        // been delivered by now, so nothing arriving proves a denied read is silent.
        await Task.Delay(TimeSpan.FromSeconds(2));

        Assert.Empty(_denials.Denied);
    }

    [Fact]
    public async Task A_token_with_two_role_groups_has_no_role()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, IdentityMeRoute)
            .WithStaffToken(TestTokens.Create("guard.ishara", Roles.Guard, Roles.NetworkController));

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_denials.Denied);
    }

    [Fact]
    public async Task A_token_with_no_catalogue_group_has_no_role()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, IdentityMeRoute)
            .WithStaffToken(TestTokens.Create("guard.ishara", "scheduler"));

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_token_for_an_unknown_subject_is_refused()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, IdentityMeRoute)
            .WithStaffToken(TestTokens.Create("nobody.unknown", Roles.Guard));

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_request_with_no_token_to_a_staff_endpoint_is_unauthorized_with_problem_details()
    {
        using var response = await _client.GetAsync(IdentityMeRoute);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(401, document.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task A_controller_token_sees_its_staff_id_username_and_role()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, IdentityMeRoute)
            .WithStaffToken(TestTokens.Create("controller.mahesh", Roles.NetworkController));

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CurrentStaffMember>();
        Assert.NotNull(body);
        Assert.Equal(Guid.Parse(ControllerStaffId), body.StaffId);
        Assert.Equal("controller.mahesh", body.Username);
        Assert.Equal(Roles.NetworkController, body.Role);
    }

    [Fact]
    public async Task An_endpoint_with_no_classification_refuses_an_anonymous_caller()
    {
        using var response = await _client.GetAsync(TestEndpoints.UnclassifiedRoute);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_network_endpoint_is_served_anonymously()
    {
        using var response = await _client.GetAsync($"{ApiRoutes.BasePath}/network/stations");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task WaitForDeliveryAsync()
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);

        while (_denials.Denied.Count == 0 && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
    }

    private sealed record CurrentStaffMember(Guid StaffId, string Username, string Role);
}
