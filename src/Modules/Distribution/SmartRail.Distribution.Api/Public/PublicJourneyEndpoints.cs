using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SmartRail.Distribution.Application.Public;
using SmartRail.Distribution.Application.Public.GetJourney;
using SmartRail.SharedKernel.Api;
using SmartRail.SharedKernel.Api.Authorization;

namespace SmartRail.Distribution.Api.Public;

/// <summary>
/// The journey detail: one journey with every stop a passenger or a station display can open. It is
/// a public read endpoint, so it is served anonymously and carries the public rate limit (design
/// D2, D7).
/// </summary>
public sealed class PublicJourneyEndpoints : ICarterModule
{
    /// <inheritdoc />
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/public").WithTags("Public").AllowAnonymousPublic();

        group
            .MapGet("/journeys/{id:guid}", GetJourneyAsync)
            .WithName("GetPublicJourney")
            .WithDescription(
                "One journey with every stop in sequence order: the calling flag, the scheduled and "
                + "expected times, the actual departure, the current platform and when it changed. A "
                + "stop the journey passes without calling carries no times and no platform. The "
                + "journey also carries the last station it passed, the station it calls at next, "
                + "minutes early or late, its status, guidance, the last-updated time and the time it "
                + "was corrected.")
            .Produces<PublicEnvelope<PublicJourneyView>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> GetJourneyAsync(
        Guid id,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetPublicJourneyQuery(id), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblemDetails();
    }
}
