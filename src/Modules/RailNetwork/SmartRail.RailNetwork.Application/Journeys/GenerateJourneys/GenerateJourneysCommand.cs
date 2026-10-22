using SmartRail.SharedKernel.Application.Messaging;

namespace SmartRail.RailNetwork.Application.Journeys.GenerateJourneys;

/// <summary>
/// Ensures every service has a journey for today and tomorrow in the railway's time zone. Running
/// it again creates no duplicates (journey-generation spec).
/// </summary>
public sealed record GenerateJourneysCommand : ICommand;
