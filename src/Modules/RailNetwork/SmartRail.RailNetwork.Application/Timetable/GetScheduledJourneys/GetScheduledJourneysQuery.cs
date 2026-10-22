using SmartRail.RailNetwork.Contracts;
using SmartRail.SharedKernel.Application.Messaging;

namespace SmartRail.RailNetwork.Application.Timetable.GetScheduledJourneys;

/// <summary>
/// The scheduled journeys of one railway calendar day. A missing date means today; only today and
/// tomorrow are accepted (journey-generation spec).
/// </summary>
public sealed record GetScheduledJourneysQuery(DateOnly? Date) : IQuery<IReadOnlyList<JourneyDto>>;
