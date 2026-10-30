using System.Net;
using System.Net.Http.Json;
using SmartRail.Host.IntegrationTests.Infrastructure;

namespace SmartRail.Host.IntegrationTests.Persistence;

/// <summary>
/// The shared request transaction (audit design D1, D3): a state-changing request writes through
/// more than one module's <c>DbContext</c> in one transaction, which commits when the handler
/// succeeds and rolls back when it throws or answers a 5xx. A read never opens it.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RequestTransactionTests : IDisposable
{
    private readonly PostgresFixture _postgres;
    private readonly SmartRailApiFactory _factory;
    private readonly HttpClient _client;

    public RequestTransactionTests(PostgresFixture postgres)
    {
        _postgres = postgres;
        _factory = new SmartRailApiFactory(postgres.ConnectionString);
        _client = _factory.CreateApiClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task A_throwing_request_rolls_back_writes_in_both_module_contexts()
    {
        await StartAsync();
        var staffId = Guid.CreateVersion7();
        var lineId = Guid.CreateVersion7();

        using var response = await WriteAsync(staffId, lineId, "throw");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.False(await StaffExistsAsync(staffId), "The identity write should have rolled back.");
        Assert.False(await LineExistsAsync(lineId), "The network write should have rolled back.");
    }

    [Fact]
    public async Task A_successful_request_commits_writes_from_both_module_contexts()
    {
        await StartAsync();
        var staffId = Guid.CreateVersion7();
        var lineId = Guid.CreateVersion7();

        try
        {
            using var response = await WriteAsync(staffId, lineId, "ok");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<TransactionWriteResponse>();
            Assert.NotNull(body);
            Assert.Equal(staffId, body.StaffId);
            Assert.Equal(lineId, body.LineId);
            Assert.True(await StaffExistsAsync(staffId), "The identity write should have committed.");
            Assert.True(await LineExistsAsync(lineId), "The network write should have committed.");
        }
        finally
        {
            await DeleteAsync(staffId, lineId);
        }
    }

    [Fact]
    public async Task A_request_that_answers_500_without_throwing_rolls_back_both_writes()
    {
        await StartAsync();
        var staffId = Guid.CreateVersion7();
        var lineId = Guid.CreateVersion7();

        using var response = await WriteAsync(staffId, lineId, "500");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.False(await StaffExistsAsync(staffId), "The identity write should have rolled back.");
        Assert.False(await LineExistsAsync(lineId), "The network write should have rolled back.");
    }

    [Fact]
    public async Task A_write_is_committed_by_the_time_the_response_headers_arrive()
    {
        await StartAsync();

        // The response is read only up to its headers: if the commit still ran after the pipeline
        // returned, the rows would not be visible yet and this would fail.
        for (var attempt = 0; attempt < 30; attempt++)
        {
            var staffId = Guid.CreateVersion7();
            var lineId = Guid.CreateVersion7();

            try
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    $"{TestEndpoints.TransactionWriteRoute}?staffId={staffId}&lineId={lineId}&mode=ok");

                using var response = await _client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead);

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.True(
                    await StaffExistsAsync(staffId),
                    $"Attempt {attempt}: the identity write was not committed when the headers arrived.");
                Assert.True(
                    await LineExistsAsync(lineId),
                    $"Attempt {attempt}: the network write was not committed when the headers arrived.");
            }
            finally
            {
                await DeleteAsync(staffId, lineId);
            }
        }
    }

    [Fact]
    public async Task A_read_request_has_no_request_transaction()
    {
        await StartAsync();

        using var response = await _client.GetAsync(TestEndpoints.TransactionReadRoute);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TransactionReadResponse>();
        Assert.NotNull(body);
        Assert.False(body.IsActive, "GET must not begin the request transaction.");
    }

    [Fact]
    public async Task A_request_after_a_rolled_back_one_still_works()
    {
        await StartAsync();

        var failedStaffId = Guid.CreateVersion7();
        var failedLineId = Guid.CreateVersion7();
        using (var failed = await WriteAsync(failedStaffId, failedLineId, "throw"))
        {
            Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        }

        // A connection or transaction leaked by the rolled-back request would show up here.
        var staffId = Guid.CreateVersion7();
        var lineId = Guid.CreateVersion7();

        try
        {
            using var response = await WriteAsync(staffId, lineId, "ok");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(await StaffExistsAsync(staffId), "The write after the rollback should have committed.");
            Assert.True(await LineExistsAsync(lineId), "The write after the rollback should have committed.");
            Assert.False(await StaffExistsAsync(failedStaffId));
            Assert.False(await LineExistsAsync(failedLineId));
        }
        finally
        {
            await DeleteAsync(staffId, lineId);
        }
    }

    private async Task StartAsync()
    {
        using var response = await _client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private Task<HttpResponseMessage> WriteAsync(Guid staffId, Guid lineId, string mode) =>
        _client.PostAsync(
            $"{TestEndpoints.TransactionWriteRoute}?staffId={staffId}&lineId={lineId}&mode={mode}",
            content: null);

    private async Task<bool> StaffExistsAsync(Guid id) => await ExistsAsync(
        $"""SELECT count(*) FROM identity.staff_members WHERE id = '{id}'""");

    private async Task<bool> LineExistsAsync(Guid id) => await ExistsAsync(
        $"""SELECT count(*) FROM network.lines WHERE id = '{id}'""");

    private async Task<bool> ExistsAsync(string sql) =>
        Assert.Single(await _postgres.QueryAsync(sql, reader => reader.GetInt64(0))) == 1;

    /// <summary>
    /// Leaves the shared test database as it was found. The seeding tests count the seeded lines and
    /// accounts, so a committed test row would break them.
    /// </summary>
    private async Task DeleteAsync(Guid staffId, Guid lineId)
    {
        await _postgres.ExecuteAsync($"""DELETE FROM identity.staff_members WHERE id = '{staffId}'""");
        await _postgres.ExecuteAsync($"""DELETE FROM network.lines WHERE id = '{lineId}'""");
    }
}
