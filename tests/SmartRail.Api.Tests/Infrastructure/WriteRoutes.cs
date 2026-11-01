using System.Net;
using Xunit.Sdk;

namespace SmartRail.Api.Tests.Infrastructure;

/// <summary>
/// The whole of one resource's state as a string, read through the API. Comparing the string taken
/// before a write with the string taken after it is how a test proves that a refusal wrote nothing —
/// and, for the positive control, that an accepted write changed something — without reading the
/// database (test-suite-review §5a phase C, item 2).
/// </summary>
internal static class ApiSnapshots
{
    /// <summary>The journey as the Network Controller's view shows it: status, changes and every stop.</summary>
    public static async Task<string> ControllerJourneyAsync(this ApiTarget api, Guid journeyId)
    {
        var detail = await api.DetailAsync(journeyId);

        return string.Join(
            Environment.NewLine,
            $"status={detail.Journey.Status}",
            $"changes={string.Join(',', detail.Changes.Select(change => change.Id))}",
            $"stops={string.Join(',', detail.Stops.Select(ControllerStop))}");
    }

    /// <summary>The journey as the calling guard's view shows it: its status and every departure.</summary>
    public static async Task<string> GuardJourneyAsync(this ApiTarget api, Guid journeyId)
    {
        var detail = await api.GuardDetailAsync(journeyId);

        return string.Join(
            Environment.NewLine,
            $"status={detail.Journey.Status}",
            $"stops={string.Join(',', detail.Journey.Stops.Select(GuardStop))}");
    }

    /// <summary>The provider's webhook registration: no subscription, or the one on record.</summary>
    public static async Task<string> WebhookAsync(this ApiTarget api)
    {
        using var response = await api.GetAsync(
            Urls.ProviderWebhook,
            api.Provider);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // The 404 body carries a per-request correlation ID, so the status alone is the snapshot.
            return "404";
        }

        await response.HasStatusAsync(HttpStatusCode.OK);
        var subscription = await response.ReadAsync<WebhookSubscription>();

        return $"200:{subscription.Id}:{subscription.Url}:{subscription.IsActive}";
    }

    private static string ControllerStop(JourneyStop stop) =>
        $"{stop.Sequence}:{stop.ActualDeparture:O}:{stop.ExpectedDeparture:O}:{stop.Platform?.Id}";

    private static string GuardStop(GuardStop stop) =>
        $"{stop.Sequence}:{stop.DepartureLogged}:{stop.ActualDeparture:O}";
}

/// <summary>
/// The eight write routes and the three kinds of credential, so one theory can name a route and the
/// test can send exactly the request that route expects.
/// </summary>
internal enum WriteAudience
{
    /// <summary>The Network Controller publishes and corrects changes.</summary>
    NetworkController,

    /// <summary>The Guard logs departures.</summary>
    Guard,

    /// <summary>The catering provider manages its own callback.</summary>
    Provider,
}

/// <summary>
/// One write route: its name, its method, who may call it, its request for given identifiers, and the
/// resource that request writes. The refusal rows and the positive control use the same route
/// objects, so the request they refuse and the request that proves it would have written cannot
/// drift apart.
/// </summary>
/// <param name="Name">The route as a person reads it, which is also the theory row's name.</param>
/// <param name="Method">The HTTP method the route accepts.</param>
/// <param name="Audience">The credential that may call it.</param>
/// <param name="Success">The status a valid request returns against <see cref="ApiIds"/>.</param>
/// <param name="Path">Builds the request path from ids the test owns.</param>
/// <param name="Body">Builds the request body, or <see langword="null"/> when the route binds none.</param>
/// <param name="Snapshot">Reads back the resource the route writes, through the API.</param>
internal sealed record WriteRoute(
    string Name,
    HttpMethod Method,
    WriteAudience Audience,
    HttpStatusCode Success,
    Func<ApiIds, string> Path,
    Func<ApiIds, object?> Body,
    Func<ApiTarget, ApiIds, Task<string>> Snapshot)
{
    /// <summary>Publishing a delay from a calling stop.</summary>
    public static WriteRoute PublishDelay { get; } = new(
        "POST /operations/journeys/{journeyId}/delays",
        HttpMethod.Post,
        WriteAudience.NetworkController,
        HttpStatusCode.Created,
        ids => Urls.Delays(ids.JourneyId),
        ids => new { fromStopSequence = ids.StopSequence, minutes = 10 },
        (api, ids) => api.ControllerJourneyAsync(ids.JourneyId));

    /// <summary>Cancelling a journey.</summary>
    public static WriteRoute PublishCancellation { get; } = new(
        "POST /operations/journeys/{journeyId}/cancellation",
        HttpMethod.Post,
        WriteAudience.NetworkController,
        HttpStatusCode.Created,
        ids => Urls.Cancellation(ids.JourneyId),
        _ => null,
        (api, ids) => api.ControllerJourneyAsync(ids.JourneyId));

    /// <summary>Moving a calling stop to another platform of its station.</summary>
    public static WriteRoute PublishPlatformChange { get; } = new(
        "POST /operations/journeys/{journeyId}/platform-changes",
        HttpMethod.Post,
        WriteAudience.NetworkController,
        HttpStatusCode.Created,
        ids => Urls.PlatformChanges(ids.JourneyId),
        ids => new { stopSequence = ids.StopSequence, platformId = ids.PlatformId },
        (api, ids) => api.ControllerJourneyAsync(ids.JourneyId));

    /// <summary>Ending a running journey before its terminus.</summary>
    public static WriteRoute PublishTermination { get; } = new(
        "POST /operations/journeys/{journeyId}/termination",
        HttpMethod.Post,
        WriteAudience.NetworkController,
        HttpStatusCode.Created,
        ids => Urls.Termination(ids.TerminationJourneyId),
        ids => new { lastStopSequence = ids.TerminationStopSequence },
        (api, ids) => api.ControllerJourneyAsync(ids.TerminationJourneyId));

    /// <summary>Withdrawing a published change.</summary>
    public static WriteRoute PublishCorrection { get; } = new(
        "POST /operations/changes/{changeId}/corrections",
        HttpMethod.Post,
        WriteAudience.NetworkController,
        HttpStatusCode.Created,
        ids => Urls.Corrections(ids.ChangeId),
        _ => null,
        (api, ids) => api.ControllerJourneyAsync(ids.JourneyId));

    /// <summary>Logging one departure, as the offline queue syncs it.</summary>
    public static WriteRoute LogDeparture { get; } = new(
        "POST /guard/departure-logs",
        HttpMethod.Post,
        WriteAudience.Guard,
        HttpStatusCode.Created,
        ids => Urls.DepartureLogs,
        ids => TestData.DepartureBody(ids.DepartureJourneyId, ids.DepartureStopSequence),
        (api, ids) => api.GuardJourneyAsync(ids.DepartureJourneyId));

    /// <summary>
    /// Registering or replacing the provider's callback URL. The access setup registers one first, so
    /// this request is a replacement and its success code is 200 rather than 201.
    /// </summary>
    public static WriteRoute RegisterProviderWebhook { get; } = new(
        "PUT /provider/webhook",
        HttpMethod.Put,
        WriteAudience.Provider,
        HttpStatusCode.OK,
        _ => Urls.ProviderWebhook,
        _ => new { url = "https://rail-bites.example/hooks/smartrail" },
        (api, _) => api.WebhookAsync());

    /// <summary>Deactivating the provider's callback, which the setup has registered.</summary>
    public static WriteRoute DeactivateProviderWebhook { get; } = new(
        "DELETE /provider/webhook",
        HttpMethod.Delete,
        WriteAudience.Provider,
        HttpStatusCode.NoContent,
        _ => Urls.ProviderWebhook,
        _ => null,
        (api, _) => api.WebhookAsync());

    /// <summary>Every write route the API exposes: five publishing routes, one guard route and two provider routes.</summary>
    public static IReadOnlyList<WriteRoute> All { get; } =
    [
        PublishDelay,
        PublishCancellation,
        PublishPlatformChange,
        PublishTermination,
        PublishCorrection,
        LogDeparture,
        RegisterProviderWebhook,
        DeactivateProviderWebhook,
    ];

    /// <summary>The route with this name, for a theory row that names it.</summary>
    public static WriteRoute ByName(string name) =>
        All.First(route => string.Equals(route.Name, name, StringComparison.Ordinal));

    /// <summary>The theory rows for the routes in the given audiences.</summary>
    public static TheoryData<string> Names(params WriteAudience[] audiences)
    {
        var rows = new TheoryData<string>();

        foreach (var route in All.Where(route => audiences.Contains(route.Audience)))
        {
            rows.Add(route.Name);
        }

        return rows;
    }

    /// <summary>
    /// The credential that is authenticated but may not call this route. A staff route is called
    /// with another role's token. A provider route is called with a staff token: no second provider
    /// key is seeded, and the provider policy accepts no staff token at all, so a staff credential is
    /// the wrong caller for these two routes (and the API answers 401, not 403 — see the access test).
    /// </summary>
    public Credential WrongCredential(ApiCredentials credentials) => Audience switch
    {
        WriteAudience.NetworkController => Credential.Staff(credentials.Guard),
        WriteAudience.Guard => Credential.Staff(credentials.Auditor),
        _ => Credential.Staff(credentials.Controller),
    };

    /// <summary>The credential that may call this route: the role's token, or the provider's API key.</summary>
    public Credential RightCredential(ApiCredentials credentials) => Audience switch
    {
        WriteAudience.NetworkController => Credential.Staff(credentials.Controller),
        WriteAudience.Guard => Credential.Staff(credentials.Guard),
        _ => Credential.Provider(credentials.ProviderKey),
    };

    /// <summary>Asserts that the resource this route writes is exactly as the snapshot left it.</summary>
    public static async Task AssertUnchangedAsync(ApiTarget api, WriteRoute route, ApiIds ids, string before)
    {
        var after = await api.SnapshotAsync(route, ids);

        if (!string.Equals(before, after, StringComparison.Ordinal))
        {
            throw new XunitException(
                $"The refused {route.Name} wrote something. Before:{Environment.NewLine}{before}"
                + $"{Environment.NewLine}After:{Environment.NewLine}{after}");
        }
    }

    /// <summary>
    /// Asserts that the accepted write changed the resource this route targets, which is the control
    /// the refusal rows rely on: a request the API would not accept could leave the snapshot alone
    /// without proving anything about authorization.
    /// </summary>
    public static async Task AssertChangedAsync(ApiTarget api, WriteRoute route, ApiIds ids, string before)
    {
        var after = await api.SnapshotAsync(route, ids);

        if (string.Equals(before, after, StringComparison.Ordinal))
        {
            throw new XunitException(
                $"The accepted {route.Name} changed nothing, so this request is not a real write."
                + $"{Environment.NewLine}Before and after:{Environment.NewLine}{before}");
        }
    }
}

/// <summary>The calls the access theories make on any write route, whichever kind of route it is.</summary>
internal static class WriteRouteRequests
{
    /// <summary>Sends this route's request with the given credential and ids.</summary>
    public static Task<HttpResponseMessage> SendAsync(
        this ApiTarget api,
        WriteRoute route,
        ApiIds ids,
        Credential credential) =>
        api.SendAsync(route.Method, route.Path(ids), credential, route.Body(ids));

    /// <summary>
    /// The resource this route writes, read through the API: the journey a change or departure would
    /// alter, or the provider's subscription.
    /// </summary>
    public static Task<string> SnapshotAsync(this ApiTarget api, WriteRoute route, ApiIds ids) =>
        route.Snapshot(api, ids);
}
