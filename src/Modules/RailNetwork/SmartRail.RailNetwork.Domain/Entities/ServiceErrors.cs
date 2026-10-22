using SmartRail.SharedKernel.Results;

namespace SmartRail.RailNetwork.Domain.Entities;

/// <summary>The expected failures of creating a <see cref="Service"/>.</summary>
public static class ServiceErrors
{
    public static readonly Error LineRequired =
        Error.Validation("service.line", "A service must belong to a line.");

    public static readonly Error DirectionInvalid =
        Error.Validation("service.direction", "A service's direction must be outbound or inbound.");

    public static readonly Error NameRequired = Error.Validation("service.name", "A service name is required.");

    public static readonly Error GuardRequired =
        Error.Validation("service.guard", "A service must name its assigned guard.");
}
