using System.Text.Json;
using SmartRail.LiveOperations.Application.ControllerView;
using SmartRail.LiveOperations.Domain.Changes;
using SmartRail.LiveOperations.Domain.LiveState;
using SmartRail.SharedKernel.Application.Messaging;
using SmartRail.SharedKernel.Results;
using ContractJourneyEventType = SmartRail.LiveOperations.Contracts.JourneyEventType;

namespace SmartRail.LiveOperations.Application.Publishing.PublishDelay;

/// <summary>
/// Publishes a delay of a whole number of minutes from a calling stop onward (NC-03). It takes
/// effect immediately on that stop and every later stop; the journey is locked and validated, the
/// fact applied and the change audited through the one publishing pipeline (design D2).
/// </summary>
/// <param name="JourneyId">The journey to delay.</param>
/// <param name="FromStopSequence">The calling stop the delay starts at; it must not have departed.</param>
/// <param name="Minutes">
/// The whole number of minutes late, from <see cref="StatusChange.MinDelayMinutes"/> to
/// <see cref="StatusChange.MaxDelayMinutes"/>.
/// </param>
public sealed record PublishDelayCommand(Guid JourneyId, int FromStopSequence, int Minutes)
    : ICommand<ControllerChangeResponse>;

internal sealed class PublishDelayCommandHandler(StatusChangePublisher publisher)
    : ICommandHandler<PublishDelayCommand, ControllerChangeResponse>
{
    /// <summary>The action the audit entry records; it is also the endpoint's name.</summary>
    private const string Action = "PublishDelay";

    public Task<Result<ControllerChangeResponse>> Handle(
        PublishDelayCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return publisher.PublishAsync(
            command.JourneyId,
            Action,
            ContractJourneyEventType.DelayPublished,
            (state, publishedBy, publishedAt, _) =>
                Task.FromResult(Plan(state, command, publishedBy, publishedAt)),
            cancellationToken);
    }

    /// <summary>The delay's own rules against the locked state, and the audit's affected fields.</summary>
    private static Result<StatusChangePlan> Plan(
        JourneyLiveState state,
        PublishDelayCommand command,
        Guid publishedBy,
        DateTimeOffset publishedAt)
    {
        var eligible = state.CheckDelayCanStart(command.FromStopSequence);
        if (eligible.IsFailure)
        {
            return Result.Failure<StatusChangePlan>(eligible.Error);
        }

        var created = StatusChange.PublishDelay(
            command.JourneyId,
            command.FromStopSequence,
            command.Minutes,
            publishedBy,
            publishedAt);

        if (created.IsFailure)
        {
            return Result.Failure<StatusChangePlan>(created.Error);
        }

        return Result.Success(new StatusChangePlan(
            created.Value,
            Before(state, command.FromStopSequence),
            () => After(command)));
    }

    /// <summary>The delay the starting stop shows before the change (design D4).</summary>
    private static string Before(JourneyLiveState state, int stopSequence) =>
        JsonSerializer.Serialize(new { delayMinutes = state.DelayMinutesAt(stopSequence) });

    /// <summary>The delay the change applies from its starting stop onward (design D4).</summary>
    private static string After(PublishDelayCommand command) =>
        JsonSerializer.Serialize(new
        {
            fromStopSequence = command.FromStopSequence,
            delayMinutes = command.Minutes,
        });
}
