using System.Text.Json;
using SmartRail.LiveOperations.Application.ControllerView;
using SmartRail.LiveOperations.Domain.Changes;
using SmartRail.LiveOperations.Domain.LiveState;
using SmartRail.SharedKernel.Application.Messaging;
using SmartRail.SharedKernel.Results;
using ContractJourneyEventType = SmartRail.LiveOperations.Contracts.JourneyEventType;

namespace SmartRail.LiveOperations.Application.Publishing.PublishCancellation;

/// <summary>
/// Cancels a journey that has no recorded departure (NC-04). It becomes <c>cancelled</c> at once, so
/// every stop reports no expected times and the journey no next station; the journey is locked and
/// validated, the fact applied and the change audited through the one publishing pipeline (design D2).
/// </summary>
/// <param name="JourneyId">The journey to cancel.</param>
public sealed record PublishCancellationCommand(Guid JourneyId) : ICommand<ControllerChangeResponse>;

internal sealed class PublishCancellationCommandHandler(StatusChangePublisher publisher)
    : ICommandHandler<PublishCancellationCommand, ControllerChangeResponse>
{
    /// <summary>The action the audit entry records; it is also the endpoint's name.</summary>
    private const string Action = "PublishCancellation";

    public Task<Result<ControllerChangeResponse>> Handle(
        PublishCancellationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return publisher.PublishAsync(
            command.JourneyId,
            Action,
            ContractJourneyEventType.CancellationPublished,
            (state, publishedBy, publishedAt, _) =>
                Task.FromResult(Plan(state, command, publishedBy, publishedAt)),
            cancellationToken);
    }

    /// <summary>The cancellation's own rule against the locked state, and the audit's fields.</summary>
    private static Result<StatusChangePlan> Plan(
        JourneyLiveState state,
        PublishCancellationCommand command,
        Guid publishedBy,
        DateTimeOffset publishedAt)
    {
        var eligible = state.CheckCanBeCancelled();
        if (eligible.IsFailure)
        {
            return Result.Failure<StatusChangePlan>(eligible.Error);
        }

        var created = StatusChange.PublishCancellation(command.JourneyId, publishedBy, publishedAt);

        if (created.IsFailure)
        {
            return Result.Failure<StatusChangePlan>(created.Error);
        }

        return Result.Success(new StatusChangePlan(created.Value, Before(state), () => After()));
    }

    /// <summary>The status the journey showed before the cancellation (design D4).</summary>
    private static string Before(JourneyLiveState state) =>
        JsonSerializer.Serialize(new { status = StatusText(state.Status) });

    /// <summary>The status the cancellation leaves the journey in (design D4).</summary>
    private static string After() =>
        JsonSerializer.Serialize(new { status = StatusText(JourneyLifecycleStatus.Cancelled) });

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
