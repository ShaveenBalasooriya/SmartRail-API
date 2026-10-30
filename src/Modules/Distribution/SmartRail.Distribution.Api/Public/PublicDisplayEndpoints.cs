using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SmartRail.Distribution.Application.Public;
using SmartRail.Distribution.Application.Public.Display;
using SmartRail.SharedKernel.Api;
using SmartRail.SharedKernel.Api.Authorization;

namespace SmartRail.Distribution.Api.Public;

/// <summary>
/// The station display feed: one platform's next departures and the notices of trains that left it
/// for another platform. It is a public read endpoint, so it is served anonymously and carries the
/// public rate limit (design D2, D7).
/// </summary>
public sealed class PublicDisplayEndpoints : ICarterModule
{
    /// <inheritdoc />
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/public").WithTags("Public").AllowAnonymousPublic();

        group
            .MapGet("/display/{stationId:guid}/platforms/{number}", GetPlatformFeedAsync)
            .WithName("GetPublicPlatformFeed")
            .WithDescription(
                "The next departures from one platform of one station: eight by default, at most "
                + "twenty. A journey is listed on the platform it is currently assigned, in "
                + "expected-departure order, with the same times, status, platform-change flag and "
                + "last-updated time as the passenger departures. A train moved to another platform "
                + "leaves this feed and is listed in movedNotices with the platform it moved to until "
                + "its scheduled departure has passed. An unknown station, or a platform number the "
                + "station does not have, is a 404.")
            .Produces<PublicEnvelope<PublicPlatformFeed>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> GetPlatformFeedAsync(
        Guid stationId,
        string number,
        int? count,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new GetPlatformFeedQuery(stationId, number, count),
            cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblemDetails();
    }
}
