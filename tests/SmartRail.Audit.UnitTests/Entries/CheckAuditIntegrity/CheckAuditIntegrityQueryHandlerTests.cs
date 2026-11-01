using SmartRail.Audit.Application.Entries.CheckAuditIntegrity;
using SmartRail.Audit.Domain.Entries;
using SmartRail.Audit.UnitTests.TestDoubles;

namespace SmartRail.Audit.UnitTests.Entries.CheckAuditIntegrity;

/// <summary>
/// The Auditor's chain integrity check (AUD-05) without a database: the status is the record's own
/// word, "intact" or "broken", and a broken record names the first position that failed. The domain
/// rules behind the verdict are proved by <c>AuditChainVerifierTests</c>; these tests prove the
/// handler reports them correctly.
/// </summary>
public sealed class CheckAuditIntegrityQueryHandlerTests
{
    [Fact]
    public async Task Happy_path_an_untouched_chain_is_reported_intact_with_its_entry_count()
    {
        // Arrange
        var handler = new CheckAuditIntegrityQueryHandler(
            new FakeAuditChainReader([.. TestData.Chain(3)]));

        // Act
        var result = await handler.Handle(new CheckAuditIntegrityQuery(), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("intact", result.Value.Status);
        Assert.Equal(3, result.Value.EntriesChecked);
        Assert.Null(result.Value.FirstFailingPosition);
    }

    [Fact]
    public async Task A_tampered_entry_is_reported_broken_at_the_first_failing_position()
    {
        // Arrange: position 2's link no longer matches the hash of the entry before it, which is what
        // a direct edit of the stored link leaves behind.
        var entries = TestData.Chain(3);
        entries[1] = TestData.ChainEntry(
            position: 2,
            previousHash: new string('a', AuditEntryHash.HexLength));
        var handler = new CheckAuditIntegrityQueryHandler(new FakeAuditChainReader([.. entries]));

        // Act
        var result = await handler.Handle(new CheckAuditIntegrityQuery(), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("broken", result.Value.Status);
        Assert.Equal(2, result.Value.FirstFailingPosition);
        Assert.Equal(2, result.Value.EntriesChecked);
    }

    [Fact]
    public async Task An_empty_record_is_reported_intact_with_nothing_checked()
    {
        // Arrange
        var handler = new CheckAuditIntegrityQueryHandler(new FakeAuditChainReader());

        // Act
        var result = await handler.Handle(new CheckAuditIntegrityQuery(), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("intact", result.Value.Status);
        Assert.Equal(0, result.Value.EntriesChecked);
        Assert.Null(result.Value.FirstFailingPosition);
    }

    [Fact]
    public async Task A_null_request_is_rejected()
    {
        // Arrange
        var handler = new CheckAuditIntegrityQueryHandler(new FakeAuditChainReader());

        // Act
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => handler.Handle(null!, CancellationToken.None));

        // Assert
        Assert.Equal("request", exception.ParamName);
    }
}
