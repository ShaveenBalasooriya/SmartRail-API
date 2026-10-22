using SmartRail.RailNetwork.Contracts;
using SmartRail.SharedKernel.Application.Messaging;

namespace SmartRail.RailNetwork.Application.Network.GetLines;

/// <summary>
/// Every line with its stations in the requested direction's order. A blank direction means
/// outbound; the validator rejects anything that is not outbound or inbound.
/// </summary>
public sealed record GetLinesQuery(string? Direction) : IQuery<IReadOnlyList<LineDto>>;
