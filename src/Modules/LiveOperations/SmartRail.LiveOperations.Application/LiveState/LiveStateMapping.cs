using SmartRail.LiveOperations.Contracts;
using SmartRail.LiveOperations.Domain.LiveState;
using ContractJourneyEventType = SmartRail.LiveOperations.Contracts.JourneyEventType;
using ContractJourneyLifecycleStatus = SmartRail.LiveOperations.Contracts.JourneyLifecycleStatus;
using DomainJourneyEventType = SmartRail.LiveOperations.Domain.LiveState.JourneyEventType;
using DomainJourneyLifecycleStatus = SmartRail.LiveOperations.Domain.LiveState.JourneyLifecycleStatus;

namespace SmartRail.LiveOperations.Application.LiveState;

/// <summary>
/// Maps the Domain live state to the module's public contract. The public enums stay in the
/// Contracts project and the Domain keeps its own, so the two meet only here (design D4).
/// </summary>
internal static class LiveStateMapping
{
    /// <summary>The contract form of a stored live state, with its stops in sequence order.</summary>
    public static LiveJourneyView ToView(JourneyLiveState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return new LiveJourneyView(
            state.Id,
            state.ServiceId,
            state.LineId,
            state.ServiceDate,
            ToContract(state.Status),
            state.DelayMinutes,
            state.LastDepartedStationId,
            state.NextStationId,
            state.LastUpdatedAt,
            state.LastSequenceNumber,
            state.GuardStaffId,
            state.Stops
                .OrderBy(stop => stop.Sequence)
                .Select(stop => new LiveStopView(
                    stop.Sequence,
                    stop.StationId,
                    stop.IsCalling,
                    stop.ScheduledArrival,
                    stop.ScheduledDeparture,
                    stop.ExpectedArrival,
                    stop.ExpectedDeparture,
                    stop.ActualDeparture,
                    stop.PlatformId,
                    stop.PlatformChangedAt))
                .ToArray(),
            state.CorrectedAt);
    }

    public static ContractJourneyLifecycleStatus ToContract(DomainJourneyLifecycleStatus status) => status switch
    {
        DomainJourneyLifecycleStatus.Scheduled => ContractJourneyLifecycleStatus.Scheduled,
        DomainJourneyLifecycleStatus.Running => ContractJourneyLifecycleStatus.Running,
        DomainJourneyLifecycleStatus.Cancelled => ContractJourneyLifecycleStatus.Cancelled,
        DomainJourneyLifecycleStatus.Terminated => ContractJourneyLifecycleStatus.Terminated,
        DomainJourneyLifecycleStatus.Completed => ContractJourneyLifecycleStatus.Completed,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown journey lifecycle status."),
    };

    public static ContractJourneyEventType ToContract(DomainJourneyEventType eventType) => eventType switch
    {
        DomainJourneyEventType.DepartureLogged => ContractJourneyEventType.DepartureLogged,
        DomainJourneyEventType.DelayPublished => ContractJourneyEventType.DelayPublished,
        DomainJourneyEventType.CancellationPublished => ContractJourneyEventType.CancellationPublished,
        DomainJourneyEventType.TerminationPublished => ContractJourneyEventType.TerminationPublished,
        DomainJourneyEventType.PlatformChanged => ContractJourneyEventType.PlatformChanged,
        DomainJourneyEventType.CorrectionPublished => ContractJourneyEventType.CorrectionPublished,
        DomainJourneyEventType.JourneyCompleted => ContractJourneyEventType.JourneyCompleted,
        _ => throw new ArgumentOutOfRangeException(nameof(eventType), eventType, "Unknown journey event type."),
    };

    public static DomainJourneyEventType ToDomain(ContractJourneyEventType eventType) => eventType switch
    {
        ContractJourneyEventType.DepartureLogged => DomainJourneyEventType.DepartureLogged,
        ContractJourneyEventType.DelayPublished => DomainJourneyEventType.DelayPublished,
        ContractJourneyEventType.CancellationPublished => DomainJourneyEventType.CancellationPublished,
        ContractJourneyEventType.TerminationPublished => DomainJourneyEventType.TerminationPublished,
        ContractJourneyEventType.PlatformChanged => DomainJourneyEventType.PlatformChanged,
        ContractJourneyEventType.CorrectionPublished => DomainJourneyEventType.CorrectionPublished,
        ContractJourneyEventType.JourneyCompleted => DomainJourneyEventType.JourneyCompleted,
        _ => throw new ArgumentOutOfRangeException(nameof(eventType), eventType, "Unknown journey event type."),
    };
}
