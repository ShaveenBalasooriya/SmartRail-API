namespace SmartRail.Host.IntegrationTests.Flows.Support;

/// <summary>
/// The one place that decides where the flow tests run (<c>test-suite-review.md</c> §4, "Switching
/// environments"). It mirrors <c>SmartRail.Api.Tests.Infrastructure.ApiTarget</c>, and exists here
/// because the API project references this one for the harness, so referencing its target from a flow
/// would be a project cycle.
/// </summary>
/// <remarks>
/// <para>
/// <c>SMARTRAIL_TEST_TARGET</c> unset, empty or <c>inprocess</c> is today's behaviour: the real Host in
/// this process, against the run's PostgreSQL container, on a frozen clock.
/// </para>
/// <para>
/// <c>remote</c> is not available to the flows yet, and this type says why instead of pretending:
/// </para>
/// <list type="bullet">
/// <item>
/// Every flow creates its own journey by publishing the Rail Network's <c>JourneyGenerated</c>
/// announcement into the module's outbox, and no public route generates a journey. A deployed API can
/// only be asked for the journeys it generated itself.
/// </item>
/// <item>
/// Every flow asserts exact times (<c>_flow.Now</c>, the audit entry's server time, the correction's
/// published time), which needs the Host's <c>TimeProvider</c> replaced with the flow's frozen clock —
/// a service registration only the in-process factory can make.
/// </item>
/// <item>
/// The provider channel is a stub receiver on Kestrel inside the test process. A deployed API cannot
/// reach it, so the deliveries there would have to be skipped.
/// </item>
/// </list>
/// <para>
/// So in remote mode every flow is skipped with that reason, naming any missing variables first, and no
/// container is started: a skipped flow never runs its test body, and the collection fixture reads
/// <see cref="IsRemote"/> before it starts Docker.
/// </para>
/// </remarks>
internal static class FlowTarget
{
    /// <summary>Picks the target: <c>inprocess</c> (the default) or <c>remote</c>.</summary>
    public const string TargetVariable = "SMARTRAIL_TEST_TARGET";

    /// <summary>The base URL of a deployed API, which a remote run would need.</summary>
    public const string BaseUrlVariable = "SMARTRAIL_API_URL";

    /// <summary>The Network Controller's bearer token, which a remote run would need.</summary>
    public const string ControllerTokenVariable = "SMARTRAIL_CONTROLLER_TOKEN";

    /// <summary>The Guard's bearer token, which a remote run would need.</summary>
    public const string GuardTokenVariable = "SMARTRAIL_GUARD_TOKEN";

    /// <summary>The Auditor's bearer token, which a remote run would need.</summary>
    public const string AuditorTokenVariable = "SMARTRAIL_AUDITOR_TOKEN";

    /// <summary>The catering provider's API key, which a remote run would need.</summary>
    public const string ProviderKeyVariable = "SMARTRAIL_PROVIDER_KEY";

    /// <summary>The blocker every flow shares, as a skip reason.</summary>
    private const string RemoteBlocker =
        "Remote target: the five flows need the in-process Host. Each one creates its journey through "
        + "the Rail Network's JourneyGenerated announcement (no route generates a journey), pins the "
        + "Host's clock so its times are exact, and asserts on a stub provider receiver inside the test "
        + "process. Use SMARTRAIL_TEST_TARGET=inprocess until a generation route and a reachable "
        + "provider receiver exist.";

    /// <summary>True when the run is configured for a deployed API instead of the in-process Host.</summary>
    public static bool IsRemote { get; } =
        string.Equals(
            Environment.GetEnvironmentVariable(TargetVariable),
            "remote",
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Why a flow cannot run against the configured target, or <see langword="null"/> when it can.
    /// </summary>
    public static string? MissingPrerequisite { get; } = MissingRemotePrerequisite();

    private static string? MissingRemotePrerequisite()
    {
        if (!IsRemote)
        {
            return null;
        }

        string[] required =
        [
            BaseUrlVariable,
            ControllerTokenVariable,
            GuardTokenVariable,
            AuditorTokenVariable,
            ProviderKeyVariable,
        ];

        var missing = required
            .Where(name => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)))
            .ToArray();

        return missing.Length == 0
            ? RemoteBlocker
            : $"Remote target: set {string.Join(", ", missing)} to run this test against a deployed API. "
                + RemoteBlocker;
    }
}

/// <summary>
/// A fact that is skipped, with the reason, when the configured target cannot run a flow. Every flow
/// test uses it, so a remote run skips loudly instead of failing obscurely.
/// </summary>
internal sealed class FlowFactAttribute : FactAttribute
{
    /// <summary>Marks the test skipped when the target cannot run it.</summary>
    public FlowFactAttribute()
    {
        if (FlowTarget.MissingPrerequisite is { } missing)
        {
            Skip = missing;
        }
    }
}

/// <summary>The theory form of <see cref="FlowFactAttribute"/>.</summary>
internal sealed class FlowTheoryAttribute : TheoryAttribute
{
    /// <summary>Marks the theory skipped when the target cannot run it.</summary>
    public FlowTheoryAttribute()
    {
        if (FlowTarget.MissingPrerequisite is { } missing)
        {
            Skip = missing;
        }
    }
}
