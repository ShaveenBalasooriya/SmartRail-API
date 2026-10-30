using SmartRail.LiveOperations.Contracts;

namespace SmartRail.Distribution.Application.Public;

/// <summary>
/// Builds the one public view of a journey from its live state and the names around it (design D1).
/// The builder is pure: it reads nothing, writes nothing and never throws on data it cannot resolve,
/// so an unknown station, platform or line leaves a null name rather than a failed response.
/// </summary>
public static class PublicJourneyViewBuilder
{
    /// <summary>The public view of one journey, with every stop in sequence order.</summary>
    public static PublicJourneyView Build(LiveJourneyView journey, NetworkLookup lookup)
    {
        ArgumentNullException.ThrowIfNull(journey);
        ArgumentNullException.ThrowIfNull(lookup);

        var service = lookup.Services.GetValueOrDefault(journey.ServiceId);
        var line = lookup.Lines.GetValueOrDefault(journey.LineId);

        var stops = journey.Stops
            .OrderBy(stop => stop.Sequence)
            .Select(stop => BuildStop(stop, lookup))
            .ToArray();

        return new PublicJourneyView(
            journey.JourneyId,
            service?.Name,
            new PublicLineRef(journey.LineId, line?.Code, line?.Color, line?.Name),
            service?.Direction,
            journey.ServiceDate,
            journey.Status,
            journey.DelayMinutes,
            Station(journey.LastDepartedStationId, lookup),
            Station(journey.NextStationId, lookup),
            journey.LastUpdatedAt,
            journey.CorrectedAt,
            ProvenanceOf(journey, stops),
            Guidance: null,
            stops)
        {
            ServiceId = journey.ServiceId,
        };
    }

    private static PublicStopView BuildStop(LiveStopView stop, NetworkLookup lookup)
    {
        // A stop the train passes without calling carries no times and no platform, whatever the
        // live state stores for it (passenger-departures spec, journey detail).
        if (!stop.IsCalling)
        {
            return new PublicStopView(
                stop.Sequence,
                Station(stop.StationId, lookup),
                IsCalling: false,
                ScheduledArrival: null,
                ScheduledDeparture: null,
                ExpectedArrival: null,
                ExpectedDeparture: null,
                ActualDeparture: null,
                Platform: null,
                PlatformChangedAt: null,
                Provenance: null);
        }

        return new PublicStopView(
            stop.Sequence,
            Station(stop.StationId, lookup),
            IsCalling: true,
            stop.ScheduledArrival,
            stop.ScheduledDeparture,
            stop.ExpectedArrival,
            stop.ExpectedDeparture,
            stop.ActualDeparture,
            Platform(stop.PlatformId, lookup),
            stop.PlatformChangedAt,
            stop.PlatformChangedAt is null ? null : PublicProvenance.AutomaticallyPublished);
    }

    /// <summary>
    /// The journey carries the label while it shows an uncorrected published disruption: a
    /// cancellation or a termination in force, or a stop whose platform was published and not since
    /// corrected. The live state drops a corrected change, so what remains here is uncorrected.
    /// </summary>
    private static PublicProvenance? ProvenanceOf(
        LiveJourneyView journey,
        IReadOnlyList<PublicStopView> stops)
    {
        var cancelledOrTerminated = journey.Status is
            JourneyLifecycleStatus.Cancelled or JourneyLifecycleStatus.Terminated;

        return cancelledOrTerminated || stops.Any(stop => stop.Provenance is not null)
            ? PublicProvenance.AutomaticallyPublished
            : null;
    }

    private static PublicStationRef? Station(Guid? stationId, NetworkLookup lookup) =>
        stationId is { } id ? Station(id, lookup) : null;

    private static PublicStationRef Station(Guid stationId, NetworkLookup lookup) =>
        new(stationId, lookup.Stations.GetValueOrDefault(stationId)?.Name);

    private static PublicPlatformRef? Platform(Guid? platformId, NetworkLookup lookup) =>
        platformId is { } id
            ? new PublicPlatformRef(id, lookup.Platforms.GetValueOrDefault(id)?.Number)
            : null;
}
