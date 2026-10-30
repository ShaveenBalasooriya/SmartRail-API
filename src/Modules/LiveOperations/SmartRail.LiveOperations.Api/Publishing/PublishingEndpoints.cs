using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SmartRail.LiveOperations.Application.ControllerView;
using SmartRail.LiveOperations.Application.Publishing.PublishCancellation;
using SmartRail.LiveOperations.Application.Publishing.PublishCorrection;
using SmartRail.LiveOperations.Application.Publishing.PublishDelay;
using SmartRail.LiveOperations.Application.Publishing.PublishPlatformChange;
using SmartRail.LiveOperations.Application.Publishing.PublishTermination;
using SmartRail.LiveOperations.Domain.Changes;
using SmartRail.SharedKernel.Api;
using SmartRail.SharedKernel.Api.Authorization;
using SmartRail.SharedKernel.Application.Abstractions;

namespace SmartRail.LiveOperations.Api.Publishing;

/// <summary>
/// The Network Controller's writes: publishing a change to a journey. Only the Network Controller
/// may call them, so a Guard or Auditor token is refused and the attempt recorded as denied
/// (status-publication spec, controller-only writes).
/// </summary>
public sealed class PublishingEndpoints : ICarterModule
{
    /// <inheritdoc />
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // One policy for every published change: the "Guard tries to publish" denial is a single
        // rule on this group rather than one per route (design D1).
        var journeys = app
            .MapGroup("/operations/journeys")
            .WithTags("Operations")
            .RequireStaffRole(Roles.NetworkController);

        journeys.MapPost("/{journeyId}/delays", PublishDelayAsync)
            .WithName("PublishDelay")
            .WithDescription(
                $"Publishes a delay of {StatusChange.MinDelayMinutes} to "
                + $"{StatusChange.MaxDelayMinutes} minutes from a calling stop that has not departed. "
                + "It takes effect at once on that stop and every later stop and is audited. "
                + "Access: staff, network_controller.")
            .Accepts<PublishDelayRequest>("application/json")
            .Produces<ControllerChangeResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // No body is bound: the cancellation needs nothing but the journey, so an empty body and no
        // body at all are both accepted (design D1).
        journeys.MapPost("/{journeyId}/cancellation", PublishCancellationAsync)
            .WithName("PublishCancellation")
            .WithDescription(
                "Cancels a journey that has no recorded departure; it becomes cancelled at once and "
                + "is audited. A journey that has already departed must be ended as an early "
                + "termination instead. Access: staff, network_controller.")
            .Produces<ControllerChangeResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        journeys.MapPost("/{journeyId}/platform-changes", PublishPlatformChangeAsync)
            .WithName("PublishPlatformChange")
            .WithDescription(
                "Moves a calling stop that has not departed to another platform of the same station. "
                + "It takes effect at once, with the publish time as the time it became known, and is "
                + "audited. Access: staff, network_controller.")
            .Accepts<PublishPlatformChangeRequest>("application/json")
            .Produces<ControllerChangeResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        journeys.MapPost("/{journeyId}/termination", PublishTerminationAsync)
            .WithName("PublishTermination")
            .WithDescription(
                "Ends a running journey before its final stop because the train cannot continue, at a "
                + "calling stop that is not the last one and not before the last recorded departure. "
                + "It becomes terminated at once, stops after the last stop served report no expected "
                + "times, and the change is audited. A journey that has not started is cancelled "
                + "instead. Access: staff, network_controller.")
            .Accepts<PublishTerminationRequest>("application/json")
            .Produces<ControllerChangeResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // A correction names only the change it withdraws, so no body is bound: an empty body and
        // no body at all are both accepted (design D1).
        var changes = app
            .MapGroup("/operations/changes")
            .WithTags("Operations")
            .RequireStaffRole(Roles.NetworkController);

        changes.MapPost("/{changeId:guid}/corrections", PublishCorrectionAsync)
            .WithName("PublishCorrection")
            .WithDescription(
                "Withdraws a delay, cancellation, early termination or platform change published "
                + "today, keeping the original recorded and marked corrected. The journey returns to "
                + "the state it would have had without it, later changes survive, and the correction "
                + "is audited and linked to the original's audit entry. A change can be corrected "
                + "once, and a correction cannot itself be corrected. Access: staff, network_controller.")
            .Produces<ControllerChangeResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> PublishDelayAsync(
        Guid journeyId,
        PublishDelayRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new PublishDelayCommand(journeyId, request.FromStopSequence, request.Minutes),
            cancellationToken);

        return result.IsSuccess ? TypedResults.Created((string?)null, result.Value) : result.ToProblemDetails();
    }

    private static async Task<IResult> PublishCancellationAsync(
        Guid journeyId,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new PublishCancellationCommand(journeyId), cancellationToken);

        return result.IsSuccess ? TypedResults.Created((string?)null, result.Value) : result.ToProblemDetails();
    }

    private static async Task<IResult> PublishPlatformChangeAsync(
        Guid journeyId,
        PublishPlatformChangeRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new PublishPlatformChangeCommand(journeyId, request.StopSequence, request.PlatformId),
            cancellationToken);

        return result.IsSuccess ? TypedResults.Created((string?)null, result.Value) : result.ToProblemDetails();
    }

    private static async Task<IResult> PublishTerminationAsync(
        Guid journeyId,
        PublishTerminationRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new PublishTerminationCommand(journeyId, request.LastStopSequence),
            cancellationToken);

        return result.IsSuccess ? TypedResults.Created((string?)null, result.Value) : result.ToProblemDetails();
    }

    private static async Task<IResult> PublishCorrectionAsync(
        Guid changeId,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new PublishCorrectionCommand(changeId), cancellationToken);

        return result.IsSuccess ? TypedResults.Created((string?)null, result.Value) : result.ToProblemDetails();
    }
}

/// <summary>The body of a published delay.</summary>
/// <param name="FromStopSequence">The calling stop the delay starts at; it must not have departed.</param>
/// <param name="Minutes">
/// The whole number of minutes late, from <see cref="StatusChange.MinDelayMinutes"/> to
/// <see cref="StatusChange.MaxDelayMinutes"/>.
/// </param>
public sealed record PublishDelayRequest(int FromStopSequence, int Minutes);

/// <summary>The body of a published platform change.</summary>
/// <param name="StopSequence">The calling stop whose platform changes; it must not have departed.</param>
/// <param name="PlatformId">The platform to move it to, which must belong to that stop's station.</param>
public sealed record PublishPlatformChangeRequest(int StopSequence, Guid PlatformId);

/// <summary>The body of a published early termination.</summary>
/// <param name="LastStopSequence">The last calling stop the train served; it must not be the final one.</param>
public sealed record PublishTerminationRequest(int LastStopSequence);
