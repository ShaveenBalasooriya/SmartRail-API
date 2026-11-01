namespace SmartRail.Api.Tests.Infrastructure;

/// <summary>
/// The routes the tests call, written out as literals: they are the API contract under test, so the
/// tests spell them rather than borrowing a constant from the application.
/// </summary>
internal static class Urls
{
    /// <summary>The versioned base path every module is mounted under.</summary>
    public const string Base = "/api/v1";

    /// <summary>Today's journeys as the Network Controller's live view lists them.</summary>
    public const string ControllerJourneys = Base + "/operations/journeys";

    /// <summary>Published changes, which a correction names.</summary>
    public const string Changes = Base + "/operations/changes";

    /// <summary>The calling guard's own journeys.</summary>
    public const string GuardJourneys = Base + "/guard/journeys";

    /// <summary>The departure log the offline queue syncs.</summary>
    public const string DepartureLogs = Base + "/guard/departure-logs";

    /// <summary>The catering provider's callback registration.</summary>
    public const string ProviderWebhook = Base + "/provider/webhook";

    /// <summary>The public station catalogue, with each station's platforms.</summary>
    public const string Stations = Base + "/network/stations";

    /// <summary>One journey with its stops and the changes published on it.</summary>
    public static string ControllerJourney(Guid journeyId) => $"{ControllerJourneys}/{journeyId}";

    /// <summary>One journey as the calling guard sees it.</summary>
    public static string GuardJourney(Guid journeyId) => $"{GuardJourneys}/{journeyId}";

    /// <summary>Publishing a delay from a calling stop.</summary>
    public static string Delays(Guid journeyId) => $"{ControllerJourneys}/{journeyId}/delays";

    /// <summary>Cancelling a journey.</summary>
    public static string Cancellation(Guid journeyId) => $"{ControllerJourneys}/{journeyId}/cancellation";

    /// <summary>Moving a calling stop to another platform.</summary>
    public static string PlatformChanges(Guid journeyId) => $"{ControllerJourneys}/{journeyId}/platform-changes";

    /// <summary>Ending a running journey early.</summary>
    public static string Termination(Guid journeyId) => $"{ControllerJourneys}/{journeyId}/termination";

    /// <summary>Withdrawing a published change.</summary>
    public static string Corrections(Guid changeId) => $"{Changes}/{changeId}/corrections";
}
