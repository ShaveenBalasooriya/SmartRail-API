using SmartRail.Distribution.Application.Public;
using SmartRail.Distribution.Application.Streams;
using SmartRail.LiveOperations.Contracts;
using SmartRail.SharedKernel.Application.Events;

namespace SmartRail.Distribution.Infrastructure.Streams;

/// <summary>
/// Feeds the event streams (design D3): every committed journey state change becomes one envelope
/// carrying the journey's resulting public view, offered to each connection whose filter matches. The
/// view is read through the module's own read port, so a stream payload and the journey detail are
/// one shape (design D1).
/// </summary>
/// <remarks>
/// The bus delivers at least once, so an event the hub already handled is ignored rather than sent
/// again. The hub is a singleton, which is the only place that memory can live: this consumer is
/// resolved in a new scope for every delivery.
/// </remarks>
internal sealed class StreamHubConsumer(
    IStreamHub hub,
    ILiveJourneys liveJourneys,
    IPublicJourneyViews views)
    : IEventConsumer<JourneyStateChanged>
{
    /// <inheritdoc />
    public string Name => "distribution.stream-hub";

    /// <inheritdoc />
    public async Task Handle(JourneyStateChanged integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        if (hub.IsHandled(integrationEvent.EventId))
        {
            return;
        }

        var journey = await liveJourneys.GetAsync(integrationEvent.JourneyId, cancellationToken);

        // A journey the live state no longer holds has no view to send. It is left unhandled, so a
        // later redelivery can still stream it if the state appears.
        if (journey is null)
        {
            return;
        }

        // The same read supplies the assignment the Guard stream's filter matches, so deciding
        // whether a connection may see the event costs no further lookup.
        var view = (await views.BuildAsync([journey], cancellationToken))[0];

        hub.Publish(new StreamEnvelope(
            integrationEvent.StreamPosition,
            integrationEvent.EventId,
            StreamEventNames.Of(integrationEvent.EventType),
            integrationEvent.JourneyId,
            journey.GuardStaffId,
            view));
    }
}
