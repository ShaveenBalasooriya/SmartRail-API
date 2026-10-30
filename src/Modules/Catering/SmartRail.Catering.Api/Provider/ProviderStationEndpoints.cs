using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SmartRail.Catering.Application.Provider.Journeys;
using SmartRail.Catering.Application.Provider.Journeys.GetStationJourneys;
using SmartRail.Catering.Application.Provider.Stations;
using SmartRail.Catering.Application.Provider.Stations.GetProviderStation;
using SmartRail.Catering.Application.Provider.Stations.GetProviderStations;
using SmartRail.SharedKernel.Api;
using SmartRail.SharedKernel.Api.Authorization;

namespace SmartRail.Catering.Api.Provider;

/// <summary>
/// The provider read API: the stations and the journeys calling at a station, and nothing else
/// (design D3; provider-data-api spec). Every route needs a provider API key and carries the
/// provider rate limit, so no staff or passenger endpoint accepts one.
/// </summary>
public sealed class ProviderStationEndpoints : ICarterModule
{
    /// <inheritdoc />
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/provider").WithTags("Provider").RequireProvider();

        group
            .MapGet("/stations", GetStationsAsync)
            .WithName("GetProviderStations")
            .Produces<IReadOnlyList<ProviderStationResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group
            .MapGet("/stations/{id}", GetStationAsync)
            .WithName("GetProviderStation")
            .Produces<ProviderStationResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group
            .MapGet("/stations/{id}/journeys", GetStationJourneysAsync)
            .WithName("GetProviderStationJourneys")
            .Produces<IReadOnlyList<ProviderJourneyResponse>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> GetStationsAsync(
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetProviderStationsQuery(), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblemDetails();
    }

    private static async Task<IResult> GetStationAsync(
        Guid id,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetProviderStationQuery(id), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblemDetails();
    }

    private static async Task<IResult> GetStationJourneysAsync(
        Guid id,
        DateOnly? date,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetStationJourneysQuery(id, date), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblemDetails();
    }
}
