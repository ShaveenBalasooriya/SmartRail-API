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
using FromHeaderAttribute = Microsoft.AspNetCore.Mvc.FromHeaderAttribute;

namespace SmartRail.Distribution.Api.Public;

/// <summary>
/// The public live stream: one Server-Sent Events event per journey state change, optionally
/// filtered to one station or one journey (live-event-streams spec, "Public event stream"). It is a
/// public read endpoint, so it is served anonymously and the public rate limit counts the
/// connection once when it opens (design D2, D7). A client that reconnects with the standard
/// <c>Last-Event-ID</c> header is first caught up, or told to reset when its position is gone
/// (live-event-streams spec, "Resume from last event" and "Catch-up beyond the retained window").
/// </summary>
public sealed class PublicStreamEndpoints : ICarterModule
{
    /// <inheritdoc />
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/public").WithTags("Public").AllowAnonymousPublic();

        group
            .MapGet("/stream", Subscribe)
            .WithName("GetPublicStream")
            .WithDescription(
                "An open Server-Sent Events stream of journey state changes. Each event carries the "
                + "stream position as its ID, the change's event type, and the journey's resulting "
                + "live view in the same shape the read endpoints return. Without a filter it carries "
                + "every journey; station sends only the journeys calling at that station, and "
                + "journey only that journey. Sending the Last-Event-ID header of the last event "
                + "received resumes after it: the missed events are sent in order before the stream "
                + "goes live, or a reset event is sent first when that position can no longer be "
                + "resumed from. A comment heartbeat is written at least every twenty seconds, so a "
                + "client can tell a quiet connection from a dead one.")
            .Produces(StatusCodes.Status200OK, contentType: "text/event-stream")
            .ProducesValidationProblem();
    }

    /// <summary>
    /// Opens the stream for one connection. A station or journey that is not an identifier is a
    /// validation failure, the same answer the other public filters give.
    /// </summary>
    private static IResult Subscribe(
        string? station,
        string? journey,
        [FromHeader(Name = "Last-Event-ID")] string? lastEventId,
        IStreamHub hub,
        IStreamCatchUp catchUp,
        DistributionOptions options,
        IOptions<JsonOptions> jsonOptions)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (!TryReadId(station, out var stationId))
        {
            errors["station"] = ["station must be a station identifier."];
        }

        if (!TryReadId(journey, out var journeyId))
        {
            errors["journey"] = ["journey must be a journey identifier."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        return new JourneyStreamResult(
            hub,
            catchUp,
            new StreamFilter(stationId, journeyId),
            lastEventId,
            TimeSpan.FromSeconds(options.StreamHeartbeatSeconds),
            jsonOptions.Value.SerializerOptions);
    }

    /// <summary>Reads an optional identifier; an absent value filters nothing.</summary>
    private static bool TryReadId(string? value, out Guid? id)
    {
        id = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!Guid.TryParse(value, out var parsed) || parsed == Guid.Empty)
        {
            return false;
        }

        id = parsed;
        return true;
    }
}
