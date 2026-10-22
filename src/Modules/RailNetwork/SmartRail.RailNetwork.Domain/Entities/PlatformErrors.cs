using SmartRail.SharedKernel.Results;

namespace SmartRail.RailNetwork.Domain.Entities;

/// <summary>The expected failures of creating a <see cref="Platform"/>.</summary>
public static class PlatformErrors
{
    public static readonly Error StationRequired =
        Error.Validation("platform.station", "A platform must belong to a station.");
}
