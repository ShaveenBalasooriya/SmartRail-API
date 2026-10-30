using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SmartRail.LiveOperations.Application.ControllerView;
using SmartRail.LiveOperations.Application.ControllerView.GetControllerJourney;
using SmartRail.LiveOperations.Application.ControllerView.GetControllerJourneys;
using SmartRail.SharedKernel.Api;
using SmartRail.SharedKernel.Api.Authorization;
using SmartRail.SharedKernel.Application.Abstractions;

namespace SmartRail.LiveOperations.Api.ControllerView;

/// <summary>
/// The Network Controller's live view: today's journeys and one journey with its stops and changes.
/// Both are staff-only and name the Network Controller role, so a Guard or Auditor token is refused
/// (controller-live-view spec).
/// </summary>
public sealed class ControllerViewEndpoints : ICarterModule
{
    /// <inheritdoc />
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/operations").WithTags("Operations");

        group.MapGet("/journeys", GetJourneysAsync)
            .WithName("GetControllerJourneys")
            .Produces<IReadOnlyList<ControllerJourneyResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireStaffRole(Roles.NetworkController);

        // The ID is bound as a Guid rather than constrained in the route, so a malformed ID is a
        // 400 naming the request, not a route that matches nothing.
        group.MapGet("/journeys/{journeyId}", GetJourneyAsync)
            .WithName("GetControllerJourney")
            .Produces<ControllerJourneyDetailResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireStaffRole(Roles.NetworkController);
    }

    private static async Task<IResult> GetJourneysAsync(ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetControllerJourneysQuery(), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblemDetails();
    }

    private static async Task<IResult> GetJourneyAsync(
        Guid journeyId,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetControllerJourneyQuery(journeyId), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblemDetails();
    }
}
