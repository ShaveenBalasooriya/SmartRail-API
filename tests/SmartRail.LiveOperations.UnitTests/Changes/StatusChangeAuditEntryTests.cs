using SmartRail.LiveOperations.Domain.Changes;

namespace SmartRail.LiveOperations.UnitTests.Changes;

/// <summary>
/// The audit entry a published change keeps (controller-operations package decision D-8): it starts
/// empty and is attached exactly once by the publishing pipeline, so a later correction can link to
/// it.
/// </summary>
public sealed class StatusChangeAuditEntryTests
{
    private static readonly DateTimeOffset PublishedAt = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_new_change_has_no_audit_entry_yet()
    {
        var change = PublishCancellation();

        Assert.Null(change.AuditEntryId);
    }

    [Fact]
    public void Attaching_an_audit_entry_records_it()
    {
        var change = PublishCancellation();
        var entryId = Guid.CreateVersion7();

        change.AttachAuditEntry(entryId);

        Assert.Equal(entryId, change.AuditEntryId);
    }

    [Fact]
    public void Attaching_the_same_audit_entry_again_changes_nothing()
    {
        var change = PublishCancellation();
        var entryId = Guid.CreateVersion7();

        change.AttachAuditEntry(entryId);
        change.AttachAuditEntry(entryId);

        Assert.Equal(entryId, change.AuditEntryId);
    }

    [Fact]
    public void Attaching_a_different_audit_entry_is_refused()
    {
        var change = PublishCancellation();
        change.AttachAuditEntry(Guid.CreateVersion7());

        Assert.Throws<InvalidOperationException>(() => change.AttachAuditEntry(Guid.CreateVersion7()));
    }

    [Fact]
    public void An_empty_audit_entry_is_refused()
    {
        var change = PublishCancellation();

        Assert.Throws<ArgumentException>(() => change.AttachAuditEntry(Guid.Empty));
        Assert.Null(change.AuditEntryId);
    }

    private static StatusChange PublishCancellation() =>
        StatusChange.PublishCancellation(Guid.CreateVersion7(), Guid.CreateVersion7(), PublishedAt).Value;
}
