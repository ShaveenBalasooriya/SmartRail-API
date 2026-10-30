using Carter;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using SmartRail.Distribution.Api.Streams;
using SmartRail.Distribution.Application.Abstractions;
using SmartRail.Distribution.Application.Streams;
using SmartRail.SharedKernel.Api.Authorization;
using SmartRail.SharedKernel.Application.Abstractions;
using FromHeaderAttribute = Microsoft.AspNetCore.Mvc.FromHeaderAttribute;

namespace SmartRail.Distribution.Api.Guard;

/// <summary>
/// The Guard's live stream: one Server-Sent Events event per state change on a journey assigned to
/// the calling guard, in the same frame format the public stream uses (live-event-streams spec,
/// "Guard stream scoped to own journeys"; design D2). It is guard-only, so a controller or auditor
/// token is refused by the staff policy before the handler runs, and an unauthenticated caller is
/// challenged. The guard is identified by the credential alone: the staff id from the token scopes
/// the stream, and nothing about the caller is written to a frame or a log.
/// </summary>
public sealed class GuardStreamEndpoints : ICarterModule
{
    /// <inheritdoc />
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/guard").WithTags("Guard").RequireStaffRole(Roles.Guard);

        group
            .MapGet("/stream", Subscribe)
            .WithName("GetGuardStream")
            .WithDescription(
                "An open Server-Sent Events stream of state changes on the journeys assigned to the "
                + "calling guard, in the same frame format as the public stream: each event carries "
                + "the stream position as its ID, the change's event type, and the journey's "
                + "resulting live view. No other guard's journey is ever sent. Sending the "
                + "Last-Event-ID header of the last event received resumes after it: the missed "
                + "events of the guard's own journeys are sent in order before the stream goes live, "
                + "or a reset event is sent first when that position can no longer be resumed from. A "
                + "comment heartbeat is written at least every twenty seconds. Access: staff, guard.")
            .Produces(StatusCodes.Status200OK, contentType: "text/event-stream")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
    }

    /// <summary>
    /// Opens the stream for one connection, scoped to the guard the credential identifies.
    /// </summary>
    private static IResult Subscribe(
        [FromHeader(Name = "Last-Event-ID")] string? lastEventId,
        IStreamHub hub,
        IStreamCatchUp catchUp,
        ICurrentUser currentUser,
        DistributionOptions options,
        IOptions<JsonOptions> jsonOptions)
    {
        // The Guard policy requires the staff claim, so a caller that reaches this handler has one.
        // A caller without it is refused rather than served an unscoped stream.
        if (currentUser.StaffId is not { } guardStaffId)
        {
            return TypedResults.Forbid();
        }

        return new JourneyStreamResult(
            hub,
            catchUp,
            new StreamFilter(GuardStaffId: guardStaffId),
            lastEventId,
            TimeSpan.FromSeconds(options.StreamHeartbeatSeconds),
            jsonOptions.Value.SerializerOptions);
    }
}
