using SmartRail.Audit.Application.Entries.GetAuditEntry;
using SmartRail.Audit.UnitTests.TestDoubles;

namespace SmartRail.Audit.UnitTests.Entries.GetAuditEntry;

/// <summary>
/// One entry in full (AUD-03): a known identifier returns it, an unknown one is the not-found
/// failure the API turns into a 404.
/// </summary>
public sealed class GetAuditEntryQueryHandlerTests
{
    [Fact]
    public async Task Happy_path_returns_the_entry()
    {
        // Arrange
        var entry = TestData.Entry(chainPosition: 4, action: "publish_cancellation");
        var handler = new GetAuditEntryQueryHandler(new FakeAuditEntryReader(entry));

        // Act
        var result = await handler.Handle(new GetAuditEntryQuery(entry.Id), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Same(entry, result.Value);
        Assert.Equal(4, result.Value.ChainPosition);
        Assert.Equal("publish_cancellation", result.Value.Action);
    }

    [Fact]
    public async Task An_unknown_identifier_is_not_found()
    {
        // Arrange
        var handler = new GetAuditEntryQueryHandler(new FakeAuditEntryReader());

        // Act
        var result = await handler.Handle(new GetAuditEntryQuery(Guid.CreateVersion7()), CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("audit_entry.not_found", result.Error.Code);
        Assert.Equal(SmartRail.SharedKernel.Results.ErrorType.NotFound, result.Error.Type);
    }

    [Fact]
    public async Task A_null_request_is_rejected()
    {
        // Arrange
        var handler = new GetAuditEntryQueryHandler(new FakeAuditEntryReader());

        // Act
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => handler.Handle(null!, CancellationToken.None));

        // Assert
        Assert.Equal("request", exception.ParamName);
    }
}
