using SmartRail.RailNetwork.Contracts;
using SmartRail.SharedKernel.Application.Messaging;

namespace SmartRail.RailNetwork.Application.Timetable.GetService;

/// <summary>One timetable service with its stops in sequence order. An unknown ID is not found.</summary>
public sealed record GetServiceQuery(Guid ServiceId) : IQuery<ServiceDto>;
