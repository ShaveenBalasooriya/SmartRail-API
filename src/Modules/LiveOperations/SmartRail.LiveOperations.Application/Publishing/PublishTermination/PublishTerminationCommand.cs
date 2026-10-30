using System.Text.Json;
using SmartRail.LiveOperations.Application.ControllerView;
using SmartRail.LiveOperations.Domain.Changes;
using SmartRail.LiveOperations.Domain.LiveState;
using SmartRail.SharedKernel.Application.Messaging;
using SmartRail.SharedKernel.Results;
using ContractJourneyEventType = SmartRail.LiveOperations.Contracts.JourneyEventType;

namespace SmartRail.LiveOperations.Application.Publishing.PublishTermination;

/// <summary>
/// Ends a running journey before its final stop because the train cannot continue (design D7). It
/// becomes <c>terminated</c> at once, so the stops after the last one served report no expected times
/// and the journey no next station; the journey is locked and validated, the fact applied and the
/// change audited through the one publishing pipeline (design D2).
/// </summary>
/// <param name="JourneyId">The journey to end early.</param>
/// <param name="LastStopSequence">The last calling stop the train served; it must not be the final one.</param>
public sealed record PublishTerminationCommand(Guid JourneyId, int LastStopSequence)
    : ICommand<ControllerChangeResponse>;

internal sealed class PublishTerminationCommandHandler(StatusChangePublisher publisher)
    : ICommandHandler<PublishTerminationCommand, ControllerChangeResponse>
{
    /// <summary>The action the audit entry records; it is also the endpoint's name.</summary>
    private const string Action = "PublishTermination";

    public Task<Result<ControllerChangeResponse>> Handle(
        PublishTerminationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return publisher.PublishAsync(
            command.JourneyId,
            Action,
            ContractJourneyEventType.TerminationPublished,
            (state, publishedBy, publishedAt, _) =>
                Task.FromResult(Plan(state, command, publishedBy, publishedAt)),
            cancellationToken);
    }

    /// <summary>The termination's own rules against the locked state, and the audit's fields.</summary>
    private static Result<StatusChangePlan> Plan(
        JourneyLiveState state,
        PublishTerminationCommand command,
        Guid publishedBy,
        DateTimeOffset publishedAt)
    {
        var eligible = state.CheckCanBeTerminated(command.LastStopSequence);
        if (eligible.IsFailure)
        {
            return Result.Failure<StatusChangePlan>(eligible.Error);
        }

        var created = StatusChange.PublishTermination(
            command.JourneyId,
            command.LastStopSequence,
            publishedBy,
            publishedAt);

        if (created.IsFailure)
        {
            return Result.Failure<StatusChangePlan>(created.Error);
        }

        return Result.Success(new StatusChangePlan(
            created.Value,
            Before(state),
            () => After(command.LastStopSequence)));
    }

    /// <summary>The status the journey showed before the termination (design D4).</summary>
    private static string Before(JourneyLiveState state) =>
        JsonSerializer.Serialize(new { status = StatusText(state.Status) });

    /// <summary>The status the termination leaves the journey in, and the last stop it served (design D4).</summary>
    private static string After(int lastStopSequence) =>
        JsonSerializer.Serialize(new
        {
            status = StatusText(JourneyLifecycleStatus.Terminated),
            lastStopSequence,
        });

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
