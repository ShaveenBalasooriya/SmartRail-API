using SmartRail.SharedKernel.Primitives;
using SmartRail.SharedKernel.Results;

namespace SmartRail.LiveOperations.Domain.Changes;

/// <summary>
/// One change a Network Controller published on a journey, stored in
/// <c>operations.status_changes</c>. The row is never edited: a mistake is withdrawn by inserting a
/// <see cref="StatusChangeType.Correction"/> that references it, so the original stays on record and
/// the calculator ignores it (design D3). <see cref="FactOrder"/> is the order the row was committed
/// in, which is what "most recent" means when facts from more than one table are combined (D-6 of
/// the controller-operations package); the database sequence assigns it on insert.
/// </summary>
public sealed class StatusChange : Entity<Guid>
{
    /// <summary>The shortest delay a controller may publish, in whole minutes.</summary>
    public const int MinDelayMinutes = 1;

    /// <summary>The longest delay a controller may publish, in whole minutes.</summary>
    public const int MaxDelayMinutes = 720;

    private StatusChange()
    {
    }

    private StatusChange(
        Guid id,
        Guid journeyId,
        StatusChangeType type,
        int? stopSequence,
        int? delayMinutes,
        Guid? newPlatformId,
        Guid? previousPlatformId,
        Guid? correctsChangeId,
        ChangeProvenance provenance,
        Guid publishedBy,
        DateTimeOffset publishedAt)
        : base(id)
    {
        JourneyId = journeyId;
        Type = type;
        StopSequence = stopSequence;
        DelayMinutes = delayMinutes;
        NewPlatformId = newPlatformId;
        PreviousPlatformId = previousPlatformId;
        CorrectsChangeId = correctsChangeId;
        Provenance = provenance;
        PublishedBy = publishedBy;
        PublishedAt = publishedAt;
    }

    public Guid JourneyId { get; private set; }

    public StatusChangeType Type { get; private set; }

    /// <summary>The stop a delay, platform change or termination refers to; <c>null</c> for the others.</summary>
    public int? StopSequence { get; private set; }

    /// <summary>The whole number of minutes of a delay; <c>null</c> for every other type.</summary>
    public int? DelayMinutes { get; private set; }

    /// <summary>The platform the stop moves to; <c>null</c> for every other type.</summary>
    public Guid? NewPlatformId { get; private set; }

    /// <summary>The platform the stop had when the change was published, when it was known.</summary>
    public Guid? PreviousPlatformId { get; private set; }

    /// <summary>The change this row corrects; <c>null</c> unless this is a correction.</summary>
    public Guid? CorrectsChangeId { get; private set; }

    public ChangeProvenance Provenance { get; private set; }

    /// <summary>The staff identifier of the controller who published the change.</summary>
    public Guid PublishedBy { get; private set; }

    /// <summary>The server time the change was published; for a platform change it is also when the change became known.</summary>
    public DateTimeOffset PublishedAt { get; private set; }

    /// <summary>
    /// The audit entry this change produced, attached once the publishing pipeline has recorded it.
    /// Audit entries live in another module's schema, so this is a plain identifier with no foreign
    /// key (controller-operations package decision D-8); a correction reads it to link its own entry
    /// to the original's (design D4).
    /// </summary>
    public Guid? AuditEntryId { get; private set; }

    /// <summary>The commit order, assigned by the database when the row is inserted.</summary>
    public long FactOrder { get; private set; }

    /// <summary>Publishes a delay of whole minutes from a calling stop onward (NC-03).</summary>
    public static Result<StatusChange> PublishDelay(
        Guid journeyId,
        int stopSequence,
        int delayMinutes,
        Guid publishedBy,
        DateTimeOffset publishedAt)
    {
        var invalid = ValidatePublisher(journeyId, publishedBy, publishedAt);
        if (invalid is not null)
        {
            return Result.Failure<StatusChange>(invalid);
        }

        if (stopSequence < 1)
        {
            return Result.Failure<StatusChange>(StatusChangeErrors.DelayStopSequenceRequired);
        }

        if (delayMinutes is < MinDelayMinutes or > MaxDelayMinutes)
        {
            return Result.Failure<StatusChange>(StatusChangeErrors.DelayMinutesOutOfRange);
        }

        return Result.Success(new StatusChange(
            Guid.CreateVersion7(),
            journeyId,
            StatusChangeType.Delay,
            stopSequence,
            delayMinutes,
            newPlatformId: null,
            previousPlatformId: null,
            correctsChangeId: null,
            ChangeProvenance.AutomaticallyPublished,
            publishedBy,
            publishedAt));
    }

    /// <summary>Cancels a journey from end to end (NC-04).</summary>
    public static Result<StatusChange> PublishCancellation(
        Guid journeyId,
        Guid publishedBy,
        DateTimeOffset publishedAt)
    {
        var invalid = ValidatePublisher(journeyId, publishedBy, publishedAt);
        if (invalid is not null)
        {
            return Result.Failure<StatusChange>(invalid);
        }

        return Result.Success(new StatusChange(
            Guid.CreateVersion7(),
            journeyId,
            StatusChangeType.Cancellation,
            stopSequence: null,
            delayMinutes: null,
            newPlatformId: null,
            previousPlatformId: null,
            correctsChangeId: null,
            ChangeProvenance.AutomaticallyPublished,
            publishedBy,
            publishedAt));
    }

    /// <summary>Ends a running journey at the last stop the train served (design D7).</summary>
    public static Result<StatusChange> PublishTermination(
        Guid journeyId,
        int lastStopSequence,
        Guid publishedBy,
        DateTimeOffset publishedAt)
    {
        var invalid = ValidatePublisher(journeyId, publishedBy, publishedAt);
        if (invalid is not null)
        {
            return Result.Failure<StatusChange>(invalid);
        }

        if (lastStopSequence < 1)
        {
            return Result.Failure<StatusChange>(StatusChangeErrors.TerminationStopSequenceRequired);
        }

        return Result.Success(new StatusChange(
            Guid.CreateVersion7(),
            journeyId,
            StatusChangeType.Termination,
            lastStopSequence,
            delayMinutes: null,
            newPlatformId: null,
            previousPlatformId: null,
            correctsChangeId: null,
            ChangeProvenance.AutomaticallyPublished,
            publishedBy,
            publishedAt));
    }

    /// <summary>Moves a calling stop to another platform of its station (NC-05).</summary>
    public static Result<StatusChange> PublishPlatformChange(
        Guid journeyId,
        int stopSequence,
        Guid newPlatformId,
        Guid? previousPlatformId,
        Guid publishedBy,
        DateTimeOffset publishedAt)
    {
        var invalid = ValidatePublisher(journeyId, publishedBy, publishedAt);
        if (invalid is not null)
        {
            return Result.Failure<StatusChange>(invalid);
        }

        if (stopSequence < 1)
        {
            return Result.Failure<StatusChange>(StatusChangeErrors.PlatformStopSequenceRequired);
        }

        if (newPlatformId == Guid.Empty)
        {
            return Result.Failure<StatusChange>(StatusChangeErrors.NewPlatformRequired);
        }

        if (previousPlatformId == Guid.Empty)
        {
            return Result.Failure<StatusChange>(StatusChangeErrors.PreviousPlatformInvalid);
        }

        return Result.Success(new StatusChange(
            Guid.CreateVersion7(),
            journeyId,
            StatusChangeType.PlatformChange,
            stopSequence,
            delayMinutes: null,
            newPlatformId,
            previousPlatformId,
            correctsChangeId: null,
            ChangeProvenance.AutomaticallyPublished,
            publishedBy,
            publishedAt));
    }

    /// <summary>Withdraws a published change, keeping it on record (NC-06, S-12).</summary>
    public static Result<StatusChange> PublishCorrection(
        Guid journeyId,
        Guid correctsChangeId,
        Guid publishedBy,
        DateTimeOffset publishedAt)
    {
        var invalid = ValidatePublisher(journeyId, publishedBy, publishedAt);
        if (invalid is not null)
        {
            return Result.Failure<StatusChange>(invalid);
        }

        if (correctsChangeId == Guid.Empty)
        {
            return Result.Failure<StatusChange>(StatusChangeErrors.CorrectionTargetRequired);
        }

        return Result.Success(new StatusChange(
            Guid.CreateVersion7(),
            journeyId,
            StatusChangeType.Correction,
            stopSequence: null,
            delayMinutes: null,
            newPlatformId: null,
            previousPlatformId: null,
            correctsChangeId,
            ChangeProvenance.AutomaticallyPublished,
            publishedBy,
            publishedAt));
    }

    /// <summary>
    /// Attaches the audit entry the publication produced. A change is audited exactly once, so the
    /// same identifier may be attached again but a different one is a programming error.
    /// </summary>
    /// <exception cref="ArgumentException">The identifier is empty.</exception>
    /// <exception cref="InvalidOperationException">A different audit entry is already attached.</exception>
    public void AttachAuditEntry(Guid auditEntryId)
    {
        if (auditEntryId == Guid.Empty)
        {
            throw new ArgumentException("An audit entry identifier is required.", nameof(auditEntryId));
        }

        if (AuditEntryId is { } existing && existing != auditEntryId)
        {
            throw new InvalidOperationException(
                $"Status change {Id} already carries audit entry {existing}.");
        }

        AuditEntryId = auditEntryId;
    }

    private static Error? ValidatePublisher(
        Guid journeyId,
        Guid publishedBy,
        DateTimeOffset publishedAt)
    {
        if (journeyId == Guid.Empty)
        {
            return StatusChangeErrors.MissingJourneyId;
        }

        if (publishedBy == Guid.Empty)
        {
            return StatusChangeErrors.MissingPublisher;
        }

        if (publishedAt == default)
        {
            return StatusChangeErrors.MissingPublishedAt;
        }

        return null;
    }
}
