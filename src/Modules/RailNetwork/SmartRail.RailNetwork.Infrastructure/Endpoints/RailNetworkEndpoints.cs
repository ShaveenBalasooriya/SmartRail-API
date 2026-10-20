using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using SmartRail.RailNetwork.Application.Clock;
using SmartRail.RailNetwork.Contracts;

namespace SmartRail.RailNetwork.Infrastructure.Endpoints;

/// <summary>
/// The rail network's public read endpoints: the physical network and the scheduled journeys of a
/// day (design D4). Authorisation arrives with the identity change, so these are open for now.
/// </summary>
public static class RailNetworkEndpoints
{
    /// <summary>Maps the network endpoints onto the <c>/network</c> group and the timetable
    /// endpoint onto the <c>/timetable</c> group.</summary>
    public static RouteGroupBuilder MapRailNetworkEndpoints(
        this RouteGroupBuilder network,
        RouteGroupBuilder timetable)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(timetable);

        network
            .MapGet("/lines", GetLinesAsync)
            .WithName("GetNetworkLines")
            .Produces<IReadOnlyList<LineDto>>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        network
            .MapGet("/stations", GetStationsAsync)
            .WithName("GetNetworkStations")
            .Produces<IReadOnlyList<StationDto>>();

        network
            .MapGet("/stations/{id:guid}", GetStationAsync)
            .WithName("GetNetworkStation")
            .Produces<StationDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        timetable
            .MapGet("/journeys", GetJourneysAsync)
            .WithName("GetTimetableJourneys")
            .Produces<IReadOnlyList<JourneyDto>>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return network;
    }

    private static async Task<Results<Ok<IReadOnlyList<LineDto>>, ProblemHttpResult>> GetLinesAsync(
        string? direction,
        INetworkCatalog catalog,
        CancellationToken cancellationToken)
    {
        if (!TryParseDirection(direction, out var parsedDirection))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "The direction must be outbound or inbound.");
        }

        return TypedResults.Ok(await catalog.GetLinesAsync(parsedDirection, cancellationToken));
    }

    private static bool TryParseDirection(string? value, out ServiceDirection direction)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            direction = ServiceDirection.Outbound;
            return true;
        }

        return Enum.TryParse(value, ignoreCase: true, out direction) && Enum.IsDefined(direction);
    }

    private static async Task<IReadOnlyList<StationDto>> GetStationsAsync(
        INetworkCatalog catalog,
        CancellationToken cancellationToken)
    {
        return await catalog.GetStationsAsync(cancellationToken);
    }

    private static async Task<Results<Ok<StationDto>, ProblemHttpResult>> GetStationAsync(
        Guid id,
        INetworkCatalog catalog,
        CancellationToken cancellationToken)
    {
        var station = await catalog.GetStationAsync(id, cancellationToken);

        return station is null
            ? TypedResults.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "No station matches this identifier.")
            : TypedResults.Ok(station);
    }

    private static async Task<Results<Ok<IReadOnlyList<JourneyDto>>, ProblemHttpResult>> GetJourneysAsync(
        DateOnly? date,
        RailwayClock clock,
        ITimetable timetable,
        CancellationToken cancellationToken)
    {
        var serviceDate = date ?? clock.Today;

        if (serviceDate != clock.Today && serviceDate != clock.Tomorrow)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "The date must be today or tomorrow in the railway's time zone.");
        }

        return TypedResults.Ok(await timetable.GetScheduledJourneysAsync(serviceDate, cancellationToken));
    }
}
