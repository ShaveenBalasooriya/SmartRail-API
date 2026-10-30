using System.Text.Json;
using SmartRail.LiveOperations.Application.ControllerView;
using SmartRail.LiveOperations.Domain.Changes;
using SmartRail.LiveOperations.Domain.LiveState;
using SmartRail.RailNetwork.Contracts;
using SmartRail.SharedKernel.Application.Messaging;
using SmartRail.SharedKernel.Results;
using ContractJourneyEventType = SmartRail.LiveOperations.Contracts.JourneyEventType;

namespace SmartRail.LiveOperations.Application.Publishing.PublishCorrection;

/// <summary>
/// Withdraws a published change, keeping it on record (NC-06, S-12). A correction carries no values of
/// its own: the calculator ignores the corrected change, so the journey returns to what it would have
/// been had the original never been published (design D3). The original is read first, outside the
/// row lock, to learn its journey; inside the lock the plan re-checks that it can still be corrected
/// and points the audit entry at the entry the original produced (design D4).
/// </summary>
/// <param name="ChangeId">The published change to withdraw.</param>
public sealed record PublishCorrectionCommand(Guid ChangeId) : ICommand<ControllerChangeResponse>;

internal sealed class PublishCorrectionCommandHandler(
    StatusChangePublisher publisher,
    IStatusChangeRepository changes,
    INetworkCatalog network) : ICommandHandler<PublishCorrectionCommand, ControllerChangeResponse>
{
    /// <summary>The action the audit entry records; it is also the endpoint's name.</summary>
    private const string Action = "PublishCorrection";

    /// <summary>
    /// The statuses a correction may be published on (decision D-7). A wrong cancellation or
    /// termination is corrected on the journey that change left cancelled or terminated, so those
    /// closed statuses are accepted; a completed journey stays closed, and the shared today rule
    /// still applies.
    /// </summary>
    private static readonly JourneyLifecycleStatus[] AcceptedStatuses =
    [
        JourneyLifecycleStatus.Scheduled,
        JourneyLifecycleStatus.Running,
        JourneyLifecycleStatus.Cancelled,
        JourneyLifecycleStatus.Terminated,
    ];

    public async Task<Result<ControllerChangeResponse>> Handle(
        PublishCorrectionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // The target is read before the journey is locked, because its journey is what the pipeline
        // locks. An unknown change is not found before anything is written.
        var original = await changes.GetAsync(command.ChangeId, cancellationToken);
        if (original is null)
        {
            return Result.Failure<ControllerChangeResponse>(
                StatusChangeErrors.CorrectionTargetNotFound);
        }

        return await publisher.PublishAsync(
            original.JourneyId,
            Action,
            ContractJourneyEventType.CorrectionPublished,
            (state, publishedBy, publishedAt, planCancellationToken) =>
                PlanAsync(state, original, publishedBy, publishedAt, planCancellationToken),
            cancellationToken,
            acceptedStatuses: AcceptedStatuses);
    }

    /// <summary>
    /// The correction's own rules against the locked journey: the target still exists, is not a
    /// correction, and has not already been withdrawn. The audit's before values are the state the
    /// original established; its after values are read once the sink has recomputed.
    /// </summary>
    private async Task<Result<StatusChangePlan>> PlanAsync(
        JourneyLiveState state,
        StatusChange original,
        Guid publishedBy,
        DateTimeOffset publishedAt,
        CancellationToken cancellationToken)
    {
        // Re-read under the lock. The row is never edited, but it keeps the rule explicit: the
        // checks below run against what is recorded now, not against the request's first look.
        var target = await changes.GetAsync(original.Id, cancellationToken);
        if (target is null)
        {
            return Result.Failure<StatusChangePlan>(StatusChangeErrors.CorrectionTargetNotFound);
        }

        if (target.Type == StatusChangeType.Correction)
        {
            return Result.Failure<StatusChangePlan>(StatusChangeErrors.CorrectionOfCorrection);
        }

        // Two controllers correcting the same change queue on the journey's row lock; the second
        // sees the first's correction here. The unique index is the backstop behind this check.
        if (await changes.IsCorrectedAsync(target.Id, cancellationToken))
        {
            return Result.Failure<StatusChangePlan>(StatusChangeErrors.ChangeAlreadyCorrected);
        }

        var created = StatusChange.PublishCorrection(
            target.JourneyId,
            target.Id,
            publishedBy,
            publishedAt);

        if (created.IsFailure)
        {
            return Result.Failure<StatusChangePlan>(created.Error);
        }

        return await DescribeAsync(state, target, created.Value, cancellationToken);
    }

    /// <summary>
    /// The affected fields of the original change, reversed: what the correction withdraws (before)
    /// and what the journey is left with once the fact sink has recomputed (after), per design D4.
    /// The after value is read from the live state because the sink updates it in place after this
    /// plan is built, so a change published after the original and still in force shows through.
    /// </summary>
    private async Task<Result<StatusChangePlan>> DescribeAsync(
        JourneyLiveState state,
        StatusChange target,
        StatusChange correction,
        CancellationToken cancellationToken)
    {
        switch (target.Type)
        {
            case StatusChangeType.Cancellation:
                return Result.Success(new StatusChangePlan(
                    correction,
                    StatusJson(JourneyLifecycleStatus.Cancelled),
                    () => StatusJson(state.Status),
                    target.AuditEntryId));

            case StatusChangeType.Termination:
                return Result.Success(new StatusChangePlan(
                    correction,
                    JsonSerializer.Serialize(new
                    {
                        status = StatusText(JourneyLifecycleStatus.Terminated),
                        lastStopSequence = target.StopSequence,
                    }),
                    () => StatusJson(state.Status),
                    target.AuditEntryId));

            case StatusChangeType.Delay:
            {
                var stopSequence = target.StopSequence ?? throw NoStopRecorded(target);

                // The delay the journey showed at the original's first stop, and what it shows there
                // after the correction is recomputed.
                return Result.Success(new StatusChangePlan(
                    correction,
                    DelayJson(state.DelayMinutesAt(stopSequence)),
                    () => DelayJson(state.DelayMinutesAt(stopSequence)),
                    target.AuditEntryId));
            }

            case StatusChangeType.PlatformChange:
            {
                if (target.StopSequence is not { } sequence || state.StopAt(sequence) is not { } stop)
                {
                    return Result.Failure<StatusChangePlan>(StatusChangeErrors.PlatformStopNotCalling);
                }

                // The numbers passengers see, resolved the same way the platform-change command
                // records them: before is the platform the original moved the stop to, after is the
                // platform the stop shows once the correction has been recomputed - which is a later
                // change's platform when one is still in force.
                var platforms = await network.GetPlatformsAsync(stop.StationId, cancellationToken);

                return Result.Success(new StatusChangePlan(
                    correction,
                    PlatformJson(NumberOf(platforms, target.NewPlatformId)),
                    () => PlatformJson(NumberOf(platforms, state.StopAt(sequence)?.PlatformId)),
                    target.AuditEntryId));
            }

            default:
                // The four change types are the only ones that can be corrected; a correction was
                // refused above, and no other type exists.
                throw new InvalidOperationException(
                    $"Status change {target.Id} of type '{target.Type}' cannot be corrected.");
        }
    }

    private static string StatusJson(JourneyLifecycleStatus status) =>
        JsonSerializer.Serialize(new { status = StatusText(status) });

    private static string DelayJson(int minutes) =>
        JsonSerializer.Serialize(new { delayMinutes = minutes });

    private static string PlatformJson(string? number) =>
        JsonSerializer.Serialize(new { platform = number });

    private static InvalidOperationException NoStopRecorded(StatusChange change) =>
        new($"Status change {change.Id} of type '{change.Type}' has no stop sequence.");

    private static string? NumberOf(IReadOnlyList<PlatformDto> platforms, Guid? platformId) =>
        platformId is { } id
            ? platforms.FirstOrDefault(platform => platform.Id == id)?.Number ?? id.ToString()
            : null;

    /// <summary>The lifecycle status as the channels and the audit record name it.</summary>
    private static string StatusText(JourneyLifecycleStatus status) => status switch
    {
        JourneyLifecycleStatus.Scheduled => "scheduled",
        JourneyLifecycleStatus.Running => "running",
        JourneyLifecycleStatus.Cancelled => "cancelled",
        JourneyLifecycleStatus.Terminated => "terminated",
        JourneyLifecycleStatus.Completed => "completed",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown journey lifecycle status."),
    };
}
