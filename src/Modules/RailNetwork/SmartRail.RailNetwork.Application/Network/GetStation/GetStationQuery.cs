using SmartRail.RailNetwork.Contracts;
using SmartRail.SharedKernel.Application.Messaging;

namespace SmartRail.RailNetwork.Application.Network.GetStation;

/// <summary>One station by ID. An unknown ID is a not-found result.</summary>
public sealed record GetStationQuery(Guid StationId) : IQuery<StationDto>;
