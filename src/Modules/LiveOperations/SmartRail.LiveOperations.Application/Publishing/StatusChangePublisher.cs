using MediatR;
using SmartRail.Audit.Contracts;
using SmartRail.IdentityAccess.Contracts;
using SmartRail.LiveOperations.Application.Abstractions;
using SmartRail.LiveOperations.Application.Changes;
using SmartRail.LiveOperations.Application.ControllerView;
using SmartRail.LiveOperations.Application.LiveState;
using SmartRail.LiveOperations.Application.LiveState.ApplyJourneyFact;
using SmartRail.LiveOperations.Domain.Changes;
using SmartRail.LiveOperations.Domain.LiveState;
using SmartRail.SharedKernel.Application.Abstractions;
using SmartRail.SharedKernel.Results;
using ContractJourneyEventType = SmartRail.LiveOperations.Contracts.JourneyEventType;

namespace SmartRail.LiveOperations.Application.Publishing;

/// <summary>
/// The one write pipeline every published change runs through (design D2): lock the journey's live
/// state, check the shared rules, let the change type validate and shape its own row, store it, let
/// the fact sink recompute the state and append the journey event, record the audit entry, and hand
/// back the change in the shape the controller's history uses. Every step runs in the request's
/// transaction, which the Host commits once, so a validation failure or a failing step leaves
/// nothing behind.
/// </summary>
internal sealed class StatusChangePublisher(
    ILiveStateRepository liveStates,
    IStatusChangeRepository changes,
    IUnitOfWork unitOfWork,
    ISender sender,
    IAuditRecorder auditRecorder,
    ICurrentUser currentUser,
    IRailwayCalendar calendar,
    IStaffDirectory staff,
    TimeProvider timeProvider)
{
    /// <summary>
    /// Publishes one change of the given type. The delegate receives the locked state, the publisher
    /// and the publish time, and returns the change with the affected fields for the audit entry. It
    /// is asynchronous because a platform change resolves the stop's station through the network
    /// catalogue while the journey is locked.
    /// </summary>
    /// <param name="acceptedStatuses">
    /// The lifecycle statuses this publication accepts, or <see langword="null"/> to apply the shared
    /// rule that only a scheduled or running journey is open to changes. A correction passes its own
    /// window (decision D-7): a wrong cancellation or termination is corrected on the journey that
    /// change left cancelled or terminated, so those closed statuses are accepted, while a completed
    /// journey stays closed. Every other change keeps today's behaviour.
    /// </param>
    public async Task<Result<ControllerChangeResponse>> PublishAsync(
        Guid journeyId,
        string action,
        ContractJourneyEventType eventType,
        Func<JourneyLiveState, Guid, DateTimeOffset, CancellationToken, Task<Result<StatusChangePlan>>> plan,
        CancellationToken cancellationToken,
        IReadOnlyCollection<JourneyLifecycleStatus>? acceptedStatuses = null)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var staffId = currentUser.StaffId
            ?? throw new InvalidOperationException(
                "A change can only be published by an authenticated staff member.");

        // A joined no-op inside a request: the row lock and every write are on the request's
        // connection and commit with its audit entry (audit design D1).
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        var state = await liveStates.GetForUpdateAsync(journeyId, cancellationToken);
        if (state is null)
        {
            return Result.Failure<ControllerChangeResponse>(JourneyLiveStateErrors.NotFound(journeyId));
        }

        var publishedAt = timeProvider.GetUtcNow();

        // Shared validation first: nothing may be written for a journey the controller does not own
        // today, or one that is already closed.
        if (state.ServiceDate != calendar.DayOf(publishedAt))
        {
            return Result.Failure<ControllerChangeResponse>(StatusChangeErrors.JourneyNotToday);
        }

        if (!IsOpenForChanges(state, acceptedStatuses))
        {
            return Result.Failure<ControllerChangeResponse>(StatusChangeErrors.JourneyClosed);
        }

        var planned = await plan(state, staffId, publishedAt, cancellationToken);
        if (planned.IsFailure)
        {
            return Result.Failure<ControllerChangeResponse>(planned.Error);
        }

        var change = planned.Value.Change;

        // The row is written first: the database assigns its commit order, and the fact reader the
        // sink uses then sees exactly the fact this change records. The repository reports false only
        // when the unique index refuses a second correction of the same change.
        var stored = await changes.AddAsync(change, cancellationToken);
        if (!stored)
        {
            return Result.Failure<ControllerChangeResponse>(StatusChangeErrors.ChangeAlreadyCorrected);
        }

        var applied = await sender.Send(
            new ApplyJourneyFactCommand(
                journeyId,
                Fact: null,
                eventType,
                change.Id,
                change.PublishedAt,
                change.PublishedAt),
            cancellationToken);

        if (applied.IsFailure)
        {
            return Result.Failure<ControllerChangeResponse>(applied.Error);
        }

        // A correction's after values are only known now: the sink has just recomputed the state the
        // correction reverts. Every other change returns the values it decided up front.
        var after = planned.Value.After();

        // The audit entry joins the change in the same transaction: a failure here refuses the
        // change rather than leaving it unaudited.
        var auditEntryId = await auditRecorder.RecordAsync(
            new AuditRecordRequest(
                staffId,
                currentUser.Role,
                action,
                TargetType: "journey",
                TargetId: journeyId.ToString(),
                planned.Value.Before,
                after,
                planned.Value.RelatedEntryId),
            cancellationToken);

        // The change keeps the entry it produced, so a later correction can link its own entry to
        // this one (design D4, package decision D-8). The entity is still tracked from the insert
        // above, so this update joins the same transaction and commits with everything else.
        change.AttachAuditEntry(auditEntryId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var view = StatusChangeMapping.ToView(change, corrected: false);
        var names = await staff.GetDisplayNamesAsync([staffId], cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return Result.Success(ControllerJourneyMapping.ToChange(view, names));
    }

    /// <summary>
    /// The shared status rule, or the window the change type supplies. A change type that accepts
    /// closed journeys (only a correction today) still cannot accept one it leaves out.
    /// </summary>
    private static bool IsOpenForChanges(
        JourneyLiveState state,
        IReadOnlyCollection<JourneyLifecycleStatus>? acceptedStatuses) =>
        acceptedStatuses is null
            ? state.IsOpenForChanges
            : acceptedStatuses.Contains(state.Status);
}
