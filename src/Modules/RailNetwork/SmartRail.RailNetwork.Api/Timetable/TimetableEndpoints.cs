using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SmartRail.RailNetwork.Application.Timetable.GetScheduledJourneys;
using SmartRail.RailNetwork.Contracts;
using SmartRail.SharedKernel.Api;

namespace SmartRail.RailNetwork.Api.Timetable;

/// <summary>
/// The timetable's read endpoint: the scheduled journeys of today or tomorrow. Authorisation
/// arrives with the identity change, so this is open for now.
/// </summary>
public sealed class TimetableEndpoints : ICarterModule
{
    /// <inheritdoc />
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGroup("/timetable")
            .WithTags("Timetable")
            .MapGet("/journeys", GetJourneysAsync)
            .WithName("GetTimetableJourneys")
            .Produces<IReadOnlyList<JourneyDto>>()
            .ProducesProblem(StatusCodes.Status400BadRequest);
    }

    private static async Task<IResult> GetJourneysAsync(
        DateOnly? date,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetScheduledJourneysQuery(date), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblemDetails();
    }
}
