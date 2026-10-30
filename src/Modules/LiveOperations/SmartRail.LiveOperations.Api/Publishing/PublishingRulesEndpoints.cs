using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SmartRail.LiveOperations.Application.Publishing;
using SmartRail.LiveOperations.Application.Publishing.GetPublishingRules;
using SmartRail.SharedKernel.Api;
using SmartRail.SharedKernel.Api.Authorization;
using SmartRail.SharedKernel.Application.Abstractions;

namespace SmartRail.LiveOperations.Api.Publishing;

/// <summary>
/// The rules a controller publishes within, so the operations web shows the range the API enforces
/// without keeping a copy of it (status-publication spec). It is staff-only and names the Network
/// Controller role, like the other operations reads.
/// </summary>
public sealed class PublishingRulesEndpoints : ICarterModule
{
    /// <inheritdoc />
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/operations/publishing-rules", GetRulesAsync)
            .WithName("GetPublishingRules")
            .Produces<PublishingRulesResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireStaffRole(Roles.NetworkController);
    }

    private static async Task<IResult> GetRulesAsync(ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetPublishingRulesQuery(), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblemDetails();
    }
}
