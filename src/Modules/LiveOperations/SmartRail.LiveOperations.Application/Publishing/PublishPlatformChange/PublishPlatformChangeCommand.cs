using System.Text.Json;
using SmartRail.LiveOperations.Application.ControllerView;
using SmartRail.LiveOperations.Domain.Changes;
using SmartRail.LiveOperations.Domain.LiveState;
using SmartRail.RailNetwork.Contracts;
using SmartRail.SharedKernel.Application.Messaging;
using SmartRail.SharedKernel.Results;
using ContractJourneyEventType = SmartRail.LiveOperations.Contracts.JourneyEventType;

namespace SmartRail.LiveOperations.Application.Publishing.PublishPlatformChange;

/// <summary>
/// Moves a calling stop that has not departed to another platform of its station (NC-05, M-09). It
/// takes effect at once, with the publish time as the time the change became known; the journey is
/// locked and validated, the fact applied and the change audited through the one publishing pipeline
/// (design D2).
/// </summary>
/// <param name="JourneyId">The journey whose stop moves.</param>
/// <param name="StopSequence">The calling stop whose platform changes; it must not have departed.</param>
/// <param name="PlatformId">The platform to move it to, which must belong to that stop's station.</param>
public sealed record PublishPlatformChangeCommand(Guid JourneyId, int StopSequence, Guid PlatformId)
    : ICommand<ControllerChangeResponse>;

internal sealed class PublishPlatformChangeCommandHandler(
    StatusChangePublisher publisher,
    INetworkCatalog network) : ICommandHandler<PublishPlatformChangeCommand, ControllerChangeResponse>
{
    /// <summary>The action the audit entry records; it is also the endpoint's name.</summary>
    private const string Action = "PublishPlatformChange";

    public Task<Result<ControllerChangeResponse>> Handle(
        PublishPlatformChangeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return publisher.PublishAsync(
            command.JourneyId,
            Action,
            ContractJourneyEventType.PlatformChanged,
            (state, publishedBy, publishedAt, planCancellationToken) =>
                PlanAsync(state, command, publishedBy, publishedAt, planCancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// The platform change's own rules against the locked state and the station's platforms, and the
    /// audit's affected fields (design D4).
    /// </summary>
    private async Task<Result<StatusChangePlan>> PlanAsync(
        JourneyLiveState state,
        PublishPlatformChangeCommand command,
        Guid publishedBy,
        DateTimeOffset publishedAt,
        CancellationToken cancellationToken)
    {
        // Bind the stop first, because the station lookup needs its row; the domain rule below
        // repeats the existence and calling checks and owns the refusal it produces.
        if (state.StopAt(command.StopSequence) is not { } stop)
        {
            return Result.Failure<StatusChangePlan>(StatusChangeErrors.PlatformStopNotCalling);
        }

        var eligible = state.CheckPlatformCanChange(command.StopSequence, command.PlatformId);
        if (eligible.IsFailure)
        {
            return Result.Failure<StatusChangePlan>(eligible.Error);
        }

        // The platform has to come from the stop's own station, so another station's platform is
        // refused like an unknown one.
        var platforms = await network.GetPlatformsAsync(stop.StationId, cancellationToken);
        var platform = platforms.FirstOrDefault(candidate => candidate.Id == command.PlatformId);

        if (platform is null)
        {
            return Result.Failure<StatusChangePlan>(StatusChangeErrors.PlatformNotAtStation);
        }

        var created = StatusChange.PublishPlatformChange(
            command.JourneyId,
            command.StopSequence,
            command.PlatformId,
            stop.PlatformId,
            publishedBy,
            publishedAt);

        if (created.IsFailure)
        {
            return Result.Failure<StatusChangePlan>(created.Error);
        }

        return Result.Success(new StatusChangePlan(
            created.Value,
            Before(platforms, stop.PlatformId),
            () => After(platform)));
    }

    /// <summary>
    /// The platform the stop showed before the change, by the number passengers see (design D4). A
    /// stop without a platform has none; one the catalogue does not know falls back to its identifier.
    /// </summary>
    private static string Before(IReadOnlyList<PlatformDto> platforms, Guid? previousPlatformId) =>
        JsonSerializer.Serialize(new { platform = NumberOf(platforms, previousPlatformId) });

    /// <summary>The platform the change moves the stop to, by the number passengers see (design D4).</summary>
    private static string After(PlatformDto platform) =>
        JsonSerializer.Serialize(new { platform = platform.Number });

    private static string? NumberOf(IReadOnlyList<PlatformDto> platforms, Guid? platformId) =>
        platformId is { } id
            ? platforms.FirstOrDefault(platform => platform.Id == id)?.Number ?? id.ToString()
            : null;
}
