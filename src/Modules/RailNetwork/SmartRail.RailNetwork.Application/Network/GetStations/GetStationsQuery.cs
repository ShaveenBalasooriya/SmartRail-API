using SmartRail.RailNetwork.Contracts;
using SmartRail.SharedKernel.Application.Messaging;

namespace SmartRail.RailNetwork.Application.Network.GetStations;

/// <summary>Every station with its platforms and the lines it belongs to.</summary>
public sealed record GetStationsQuery : IQuery<IReadOnlyList<StationDto>>;
