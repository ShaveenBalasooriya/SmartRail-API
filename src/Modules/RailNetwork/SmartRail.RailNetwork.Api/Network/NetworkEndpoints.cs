using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SmartRail.RailNetwork.Application.Network.GetLines;
using SmartRail.RailNetwork.Application.Network.GetStation;
using SmartRail.RailNetwork.Application.Network.GetStations;
using SmartRail.RailNetwork.Contracts;
using SmartRail.SharedKernel.Api;
using SmartRail.SharedKernel.Api.Authorization;

namespace SmartRail.RailNetwork.Api.Network;

/// <summary>
/// The network's read endpoints: lines with their ordered stations, the stations with their
/// platforms and lines, and one station by ID. They are public data, so they are served
/// anonymously and carry the public rate limit (design D4).
/// </summary>
public sealed class NetworkEndpoints : ICarterModule
{
    /// <inheritdoc />
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/network").WithTags("Network").AllowAnonymousPublic();

        group
            .MapGet("/lines", GetLinesAsync)
            .WithName("GetNetworkLines")
            .Produces<IReadOnlyList<LineDto>>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group
            .MapGet("/stations", GetStationsAsync)
            .WithName("GetNetworkStations")
            .Produces<IReadOnlyList<StationDto>>();

        group
            .MapGet("/stations/{id:guid}", GetStationAsync)
            .WithName("GetNetworkStation")
            .Produces<StationDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> GetLinesAsync(
        string? direction,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetLinesQuery(direction), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblemDetails();
    }

    private static async Task<IResult> GetStationsAsync(ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetStationsQuery(), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblemDetails();
    }

    private static async Task<IResult> GetStationAsync(
        Guid id,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetStationQuery(id), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblemDetails();
    }
}
