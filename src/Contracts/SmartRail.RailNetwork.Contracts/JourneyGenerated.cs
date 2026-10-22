using MediatR;

namespace SmartRail.RailNetwork.Contracts;

/// <summary>
/// Announced once for each journey that generation creates (design D2, D3). It carries the full
/// stop baseline, plus the service's line, direction and guard, so a subscriber can create its live
/// state without reading the timetable.
/// </summary>
public sealed record JourneyGenerated(
    Guid JourneyId,
    Guid ServiceId,
    DateOnly ServiceDate,
    Guid LineId,
    ServiceDirection Direction,
    Guid GuardStaffId,
    IReadOnlyList<JourneyStopDto> Stops) : INotification;
