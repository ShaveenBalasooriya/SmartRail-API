using System.Net;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartRail.Host.IntegrationTests.Flows.Builders;
using SmartRail.Host.IntegrationTests.Flows.Drivers;
using SmartRail.Host.IntegrationTests.Flows.Steps;
using SmartRail.Host.IntegrationTests.Infrastructure;

namespace SmartRail.Host.IntegrationTests.Flows.Support;

/// <summary>
/// One flow's world: the real Host against real PostgreSQL, on a frozen clock, with the drivers, the
/// journey builder and the step layer a flow needs, and the cleanup of everything the flow created.
/// A flow gets its own instance and therefore its own factory and journeys, so no flow depends on
/// another one's data or on the order the suite runs in.
/// </summary>
/// <remarks>
/// <para>
/// The Host's clock is frozen at noon in the railway's time zone on today's service date. That is
/// safely inside the day — the seeded timetable lines up, a departure is neither ahead of the server
/// nor before the service date — and the frozen instant is what a refusal's recorded time has to be,
/// so the audit assertion is exact rather than "roughly now".
/// </para>
/// <para>
/// A flow reads and asserts through public interfaces only: the public API, the controller's and the
/// Guard's routes, the Auditor's API and the stub provider receiver. The single exception is
/// <see cref="FlowCleanup"/>, which removes the rows the flow added from the shared database.
/// </para>
/// </remarks>
internal sealed class FlowContext : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private readonly IReadOnlyDictionary<string, string>? _settings;
    private TestClock? _clock;
    private SmartRailApiFactory? _factory;
    private HttpClient? _client;
    private FlowCleanup? _cleanup;

    /// <param name="postgres">The shared database fixture.</param>
    /// <param name="settings">
    /// Extra Host settings, for a flow whose channels need them — the provider's key and a dispatcher
    /// schedule short enough to observe.
    /// </param>
    public FlowContext(
        PostgresFixture postgres,
        IReadOnlyDictionary<string, string>? settings = null)
    {
        _postgres = postgres;
        _settings = settings;
    }

    /// <summary>Today in the railway's time zone, the service date of every journey the flow builds.</summary>
    public DateOnly Today { get; private set; }

    /// <summary>The frozen instant the Host sees as now.</summary>
    public DateTimeOffset Now { get; private set; }

    /// <summary>Creates the flows' journeys through the real generation announcement.</summary>
    public JourneyBuilder Journeys { get; private set; } = null!;

    /// <summary>The Auditor's read surface: the audit list, one entry, and the chain integrity check.</summary>
    public AuditorDriver Auditor { get; private set; } = null!;

    /// <summary>The anonymous passenger channel.</summary>
    public PublicApiDriver Public { get; private set; } = null!;

    /// <summary>The seeded Guard's surface, for the departures a flow logs.</summary>
    public GuardDriver Guard { get; private set; } = null!;

    /// <summary>The controller writes a flow publishes, arranged as steps.</summary>
    public ChangePublishingSteps Changes { get; private set; } = null!;

    /// <summary>The catering provider's callback subscription.</summary>
    public ProviderDriver Provider { get; private set; } = null!;

    /// <summary>The five controller writes, each as a step a flow can arrange and attempt.</summary>
    public ControllerWriteSteps Writes { get; private set; } = null!;

    /// <summary>The controller's write surface, as other drivers or sessions reach it.</summary>
    public ControllerDriver ControllerWrites(StaffSession? session) => new(Client, session);

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        if (FlowTarget.IsRemote)
        {
            throw new InvalidOperationException(
                $"{nameof(FlowContext)} runs the Host in this process; {FlowTarget.TargetVariable}=remote "
                + "is skipped by the flow attributes and must never reach here.");
        }

        var configured = Environment.GetEnvironmentVariable(FlowTarget.TargetVariable);
        if (!string.IsNullOrWhiteSpace(configured)
            && !string.Equals(configured, "inprocess", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{FlowTarget.TargetVariable} is '{configured}'. Use 'inprocess' (the default) or 'remote'.");
        }

        Today = RailwayDay.Today();
        Now = RailwayDay.Noon(Today);
        _clock = new TestClock(Now);

        _factory = new SmartRailApiFactory(
            _postgres.ConnectionString,
            configureServices: services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(_clock);
            },
            settings: WithStreamHeartbeat(_settings));

        _client = _factory.CreateApiClient();

        using var ready = await _client.GetAsync("/health/ready");

        Verify.Equal(
            HttpStatusCode.OK,
            ready.StatusCode,
            "The Host should be ready, with its schema migrated, before a flow starts");

        _cleanup = await FlowCleanup.CaptureAsync(_postgres);

        Auditor = new AuditorDriver(_client, StaffDirectory.Auditor);
        Public = new PublicApiDriver(_client);
        Guard = new GuardDriver(_client, StaffDirectory.Guard);
        Journeys = new JourneyBuilder(_factory, _client, Today, Now);
        Changes = new ChangePublishingSteps(_client, Now);
        Provider = new ProviderDriver(_client);
        Writes = new ControllerWriteSteps(_client, Journeys, Now);
    }

    /// <summary>
    /// Under TestServer the response reaches the client only when the server writes its first body byte,
    /// and a quiet stream writes nothing until an event or a heartbeat, so opening a stream blocks for a
    /// whole heartbeat. A one second heartbeat keeps the open fast (verified by the Backend thread: the
    /// frames themselves arrive about 1 s after a publish). A flow may still set its own value.
    /// </summary>
    private static Dictionary<string, string> WithStreamHeartbeat(IReadOnlyDictionary<string, string>? settings)
    {
        var merged = new Dictionary<string, string>(settings ?? new Dictionary<string, string>());
        merged.TryAdd("Distribution:StreamHeartbeatSeconds", "1");
        return merged;
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (_client is null || _factory is null)
        {
            return;
        }

        if (_cleanup is not null)
        {
            await _cleanup.RunAsync(Journeys.Created, Provider.HasSubscription);
        }

        _client.Dispose();
        _factory.Dispose();
    }

    private HttpClient Client =>
        _client ?? throw new InvalidOperationException("The flow has not been initialised.");
}
