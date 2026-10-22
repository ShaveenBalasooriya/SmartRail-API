using SmartRail.SharedKernel.Results;

namespace SmartRail.RailNetwork.Domain.Entities;

/// <summary>The expected failures of creating a <see cref="Journey"/>.</summary>
public static class JourneyErrors
{
    public static readonly Error FirstStopDepartureRequired =
        Error.Validation(
            "journey.departure",
            "A service needs a first-stop scheduled departure to generate a journey.");
}
