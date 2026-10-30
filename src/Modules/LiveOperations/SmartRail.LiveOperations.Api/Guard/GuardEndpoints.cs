using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SmartRail.LiveOperations.Application.Departures;
using SmartRail.LiveOperations.Application.Departures.LogDeparture;
using SmartRail.LiveOperations.Application.Guard;
using SmartRail.LiveOperations.Application.Guard.GetGuardJourney;
using SmartRail.LiveOperations.Application.Guard.GetGuardJourneys;
using SmartRail.SharedKernel.Api;
using SmartRail.SharedKernel.Api.Authorization;
using SmartRail.SharedKernel.Application.Abstractions;

namespace SmartRail.LiveOperations.Api.Guard;

/// <summary>
/// The Guard's surface: the journeys assigned to them and the departure log the offline queue syncs.
/// All of it is guard-only, so a controller or auditor token is refused (guard-assignments spec,
/// guard-only endpoints; departure-logging design D1).
/// </summary>
public sealed class GuardEndpoints : ICarterModule
{
    /// <inheritdoc />
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/guard")
            .WithTags("Guard")
            .RequireStaffRole(Roles.Guard);

        group.MapGet("/journeys", GetJourneysAsync)
            .WithName("GetGuardJourneys")
            .WithDescription(
                "The journeys the calling guard is assigned today, in scheduled-departure order, each "
                + "with its calling stops, current platforms and whether each departure has been logged. "
                + "No other guard's journey is ever returned, and a guard with no assignment today gets "
                + "an empty list. Access: staff, guard.")
            .Produces<IReadOnlyList<GuardJourneyResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/journeys/{journeyId:guid}", GetJourneyAsync)
            .WithName("GetGuardJourney")
            .WithDescription(
                "One journey assigned to the calling guard, with its calling stops and every delay, "
                + "cancellation, termination, platform change and correction published on it, each with "
                + "the time it was published. A journey assigned to another guard, or an unknown one, is "
                + "not found rather than forbidden, so the ID reveals nothing. A journey of another "
                + "service date is served, because a queued sync may arrive after midnight. "
                + "Access: staff, guard.")
            .Produces<GuardJourneyDetailResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/departure-logs", LogDepartureAsync)
            .WithName("LogDeparture")
            .WithDescription(
                "Records the departure of one calling stop of a journey assigned to the calling guard, "
                + "with the client-generated identifier and the original device time of the tap; the "
                + "server receipt time is recorded separately. The status codes the offline app relies "
                + "on: 201 accepted; 200 identical replay, with the same record and nothing changed; "
                + "409 conflict, when the identifier was accepted with different content or the stop "
                + "already has a departure; 400 invalid, which must never be retried; 403 not assigned "
                + "to this guard; 404 unknown journey. The app clears the queue item on 200, 201, 409 "
                + "and 400 and keeps it on network errors and 5xx. Access: staff, guard.")
            .Accepts<LogDepartureRequest>("application/json")
            .Produces<DepartureLogResponse>(StatusCodes.Status201Created)
            .Produces<DepartureLogResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> GetJourneysAsync(ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetGuardJourneysQuery(), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblemDetails();
    }

    private static async Task<IResult> GetJourneyAsync(
        Guid journeyId,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetGuardJourneyQuery(journeyId), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblemDetails();
    }

    private static async Task<IResult> LogDepartureAsync(
        LogDepartureRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new LogDepartureCommand(
                request.Id,
                request.JourneyId,
                request.StopSequence,
                request.DepartedAt,
                request.DelayCause),
            cancellationToken);

        if (result.IsFailure)
        {
            return result.ToProblemDetails();
        }

        var logged = result.Value;

        // 201 for a newly accepted log, 200 for an identical replay: the app only needs to tell
        // "done" from "already done", and clears the queue item on both (design D2).
        return logged.Replayed
            ? TypedResults.Ok(logged.Log)
            : TypedResults.Created((string?)null, logged.Log);
    }
}

/// <summary>The body of a logged departure.</summary>
/// <param name="Id">The client-generated identifier, which makes a blind resend safe.</param>
/// <param name="JourneyId">The journey whose stop departed.</param>
/// <param name="StopSequence">The calling stop that departed; the final stop has none to log.</param>
/// <param name="DepartedAt">The device time of the tap, which must carry its UTC offset.</param>
/// <param name="DelayCause">One of the seven causes, or <see langword="null"/> when none was given.</param>
public sealed record LogDepartureRequest(
    Guid Id,
    Guid JourneyId,
    int StopSequence,
    string? DepartedAt,
    string? DelayCause);
