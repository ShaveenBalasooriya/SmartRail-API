using SmartRail.SharedKernel.Results;

namespace SmartRail.RailNetwork.Domain.Entities;

/// <summary>The expected failures of creating a <see cref="Station"/>.</summary>
public static class StationErrors
{
    public static readonly Error NameRequired = Error.Validation("station.name", "A station name is required.");
}
