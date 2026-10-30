using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SmartRail.Distribution.Application.Public;
using SmartRail.Distribution.Application.Public.Departures;
using SmartRail.Distribution.Application.Public.Departures.GetDeparturesBetweenStations;
using SmartRail.Distribution.Application.Public.Departures.GetStationDepartures;
using SmartRail.SharedKernel.Api;
using SmartRail.SharedKernel.Api.Authorization;

namespace SmartRail.Distribution.Api.Public;

/// <summary>
/// The passenger departures: what leaves one station for another, and what leaves a station in the
/// next window. Both are public read endpoints, so they are served anonymously and carry the public
/// rate limit (design D2, D7).
/// </summary>
public sealed class PublicDeparturesEndpoints : ICarterModule
{
    /// <inheritdoc />
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/public").WithTags("Public").AllowAnonymousPublic();

        group
            .MapGet("/departures", GetDeparturesAsync)
            .WithName("GetPublicDepartures")
            .WithDescription(
                "Upcoming journeys today that call at the origin station and later at the destination "
                + "station, ordered by expected departure. A journey that passes either station without "
                + "calling, or that reaches the destination first, is not listed, and a change of line "
                + "is never planned. Every item carries its origin times and platform, the destination "
                + "station, minutes early or late, guidance and the last-updated time.")
            .Produces<PublicEnvelope<IReadOnlyList<PublicDepartureItem>>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group
            .MapGet("/stations/{id:guid}/departures", GetStationDeparturesAsync)
            .WithName("GetPublicStationDepartures")
            .WithDescription(
                "Upcoming journeys departing the station inside the next window: two hours by default, "
                + "at most six. The interchange station returns the onward departures of every line that "
                + "serves it. A journey's final calling stop is not a departure there. Every item carries "
                + "its origin times and platform, the destination station, minutes early or late, "
                + "guidance and the last-updated time.")
            .Produces<PublicEnvelope<IReadOnlyList<PublicDepartureItem>>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> GetDeparturesAsync(
        Guid? from,
        Guid? to,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new GetDeparturesBetweenStationsQuery(from ?? Guid.Empty, to ?? Guid.Empty),
            cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblemDetails();
    }

    private static async Task<IResult> GetStationDeparturesAsync(
        Guid id,
        int? windowMinutes,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new GetStationDeparturesQuery(id, windowMinutes),
            cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblemDetails();
    }
}
