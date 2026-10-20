using SmartRail.SharedKernel.Results;

namespace SmartRail.RailNetwork.Domain.Entities;

/// <summary>The expected failures of creating a <see cref="LineStation"/>.</summary>
public static class LineStationErrors
{
    public static readonly Error LineRequired =
        Error.Validation("line_station.line", "A line station must belong to a line.");

    public static readonly Error StationRequired =
        Error.Validation("line_station.station", "A line station must name a station.");

    public static readonly Error SequenceInvalid =
        Error.Validation("line_station.sequence", "A line station's sequence must be one or greater.");
}
