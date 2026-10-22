using SmartRail.SharedKernel.Results;

namespace SmartRail.RailNetwork.Domain.Entities;

/// <summary>The expected failures of creating a <see cref="ServiceStop"/>.</summary>
public static class ServiceStopErrors
{
    public static readonly Error ServiceRequired =
        Error.Validation("service_stop.service", "A stop must belong to a service.");

    public static readonly Error StationRequired =
        Error.Validation("service_stop.station", "A stop must name a station.");

    public static readonly Error SequenceInvalid =
        Error.Validation("service_stop.sequence", "A stop's sequence must be one or greater.");

    public static readonly Error NonCallingStopHasDetails =
        Error.Validation(
            "service_stop.non_calling",
            "A non-calling stop has no times and no platform.");

    public static readonly Error PlatformRequired =
        Error.Validation("service_stop.platform", "A calling stop must name a platform.");

    public static readonly Error TimesRequired =
        Error.Validation(
            "service_stop.times",
            "A calling stop must have a scheduled arrival or departure.");

    public static readonly Error ArrivalRequired =
        Error.Validation(
            "service_stop.arrival",
            "A calling stop after the first must have a scheduled arrival.");
}
