using SmartRail.Audit.Application.Entries;
using SmartRail.Audit.Application.Entries.GetAuditEntries;
using SmartRail.Audit.Domain.Entries;
using SmartRail.Audit.UnitTests.TestDoubles;
using SmartRail.SharedKernel.Paging;

namespace SmartRail.Audit.UnitTests.Entries.GetAuditEntries;

/// <summary>
/// The Auditor's filtered, paged list (AUD-01, AUD-02) without a database: newest first, the wire
/// outcome mapped to the stored outcome, a blank outcome meaning both, and the paging the caller
/// asked for.
/// </summary>
public sealed class GetAuditEntriesQueryHandlerTests
{
    [Fact]
    public async Task Happy_path_returns_the_newest_page_with_no_filter()
    {
        // Arrange
        var oldest = TestData.Entry(chainPosition: 1);
        var middle = TestData.Entry(chainPosition: 2);
        var newest = TestData.Entry(chainPosition: 3);
        var reader = new FakeAuditEntryReader(oldest, middle, newest);
        var handler = new GetAuditEntriesQueryHandler(reader);

        // Act
        var result = await handler.Handle(
            new GetAuditEntriesQuery(ActorId: null, From: null, To: null, Outcome: null, Page: 1, PageSize: 2),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal([newest.Id, middle.Id], result.Value.Items.Select(item => item.Id));
        Assert.Equal(3, result.Value.TotalCount);
        Assert.Equal(1, result.Value.Page);
        Assert.Equal(2, result.Value.PageSize);

        var filter = Assert.IsType<AuditEntryFilter>(reader.LastFilter);
        Assert.Null(filter.ActorStaffId);
        Assert.Null(filter.From);
        Assert.Null(filter.To);
        Assert.Null(filter.Outcome);
        Assert.Equal(1, reader.LastPage!.Value.Page);
        Assert.Equal(2, reader.LastPage!.Value.PageSize);
    }

    [Theory]
    [InlineData("denied", AuditOutcome.Denied)]
    [InlineData("DENIED", AuditOutcome.Denied)]
    [InlineData("succeeded", AuditOutcome.Succeeded)]
    [InlineData("Succeeded", AuditOutcome.Succeeded)]
    public async Task An_outcome_name_selects_that_outcome(string outcome, AuditOutcome expected)
    {
        // Arrange
        var succeeded = TestData.Entry(chainPosition: 1, outcome: AuditOutcome.Succeeded);
        var denied = TestData.Entry(chainPosition: 2, outcome: AuditOutcome.Denied);
        var reader = new FakeAuditEntryReader(succeeded, denied);
        var handler = new GetAuditEntriesQueryHandler(reader);

        // Act
        var result = await handler.Handle(
            new GetAuditEntriesQuery(ActorId: null, From: null, To: null, Outcome: outcome, Page: 1, PageSize: 25),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value.Items);
        Assert.Equal(expected.ToString().ToLowerInvariant(), item.Outcome);
        Assert.Equal(expected, Assert.IsType<AuditEntryFilter>(reader.LastFilter).Outcome);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_outcome_means_no_outcome_filter(string? outcome)
    {
        // Arrange
        var succeeded = TestData.Entry(chainPosition: 1, outcome: AuditOutcome.Succeeded);
        var denied = TestData.Entry(chainPosition: 2, outcome: AuditOutcome.Denied);
        var reader = new FakeAuditEntryReader(succeeded, denied);
        var handler = new GetAuditEntriesQueryHandler(reader);

        // Act
        var result = await handler.Handle(
            new GetAuditEntriesQuery(ActorId: null, From: null, To: null, Outcome: outcome, Page: 1, PageSize: 25),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Items.Count);
        Assert.Null(Assert.IsType<AuditEntryFilter>(reader.LastFilter).Outcome);
    }

    [Fact]
    public async Task The_actor_and_time_range_reach_the_reader_and_narrow_the_result()
    {
        // Arrange
        var actor = Guid.CreateVersion7();
        var otherActor = Guid.CreateVersion7();
        var from = TestData.Now.AddHours(-2);
        var to = TestData.Now.AddHours(-1);

        var inRange = TestData.Entry(chainPosition: 1, occurredAt: TestData.Now.AddMinutes(-90), actorStaffId: actor);
        var beforeRange = TestData.Entry(chainPosition: 2, occurredAt: TestData.Now.AddHours(-3), actorStaffId: actor);
        var otherStaff = TestData.Entry(chainPosition: 3, occurredAt: TestData.Now.AddMinutes(-90), actorStaffId: otherActor);
        var reader = new FakeAuditEntryReader(inRange, beforeRange, otherStaff);
        var handler = new GetAuditEntriesQueryHandler(reader);

        // Act
        var result = await handler.Handle(
            new GetAuditEntriesQuery(actor, from, to, Outcome: null, Page: 1, PageSize: 25),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(inRange.Id, Assert.Single(result.Value.Items).Id);

        var filter = Assert.IsType<AuditEntryFilter>(reader.LastFilter);
        Assert.Equal(actor, filter.ActorStaffId);
        Assert.Equal(from, filter.From);
        Assert.Equal(to, filter.To);
    }

    [Fact]
    public async Task A_later_page_returns_only_that_page_and_the_full_count()
    {
        // Arrange
        var first = TestData.Entry(chainPosition: 1);
        var second = TestData.Entry(chainPosition: 2);
        var third = TestData.Entry(chainPosition: 3);
        var reader = new FakeAuditEntryReader(first, second, third);
        var handler = new GetAuditEntriesQueryHandler(reader);

        // Act
        var result = await handler.Handle(
            new GetAuditEntriesQuery(ActorId: null, From: null, To: null, Outcome: null, Page: 2, PageSize: 2),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(first.Id, Assert.Single(result.Value.Items).Id);
        Assert.Equal(3, result.Value.TotalCount);
        Assert.True(result.Value.HasPreviousPage);
        Assert.False(result.Value.HasNextPage);
    }

    [Fact]
    public async Task A_null_request_is_rejected()
    {
        // Arrange
        var handler = new GetAuditEntriesQueryHandler(new FakeAuditEntryReader());

        // Act
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => handler.Handle(null!, CancellationToken.None));

        // Assert
        Assert.Equal("request", exception.ParamName);
    }
}
