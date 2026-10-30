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
/// A flow gets its own instance and therefore its own factory, journeys and outbox baseline, so no
/// flow depends on another one's data or on the order the suite runs in.
/// </summary>
/// <remarks>
/// The Host's clock is frozen at noon in the railway's time zone on today's service date. That is
/// safely inside the day — the seeded timetable lines up, a departure is neither ahead of the server
/// nor before the service date — and the frozen instant is what a refusal's recorded time has to be,
/// so the audit assertion is exact rather than "roughly now".
/// </remarks>
internal sealed class FlowContext : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private readonly Action<IServiceCollection>? _configureServices;
    private readonly IReadOnlyDictionary<string, string>? _settings;
    private TestClock? _clock;
    private SmartRailApiFactory? _factory;
    private HttpClient? _client;
    private long _identityBaseline;
    private long _auditBaseline;

    /// <param name="postgres">The shared database fixture.</param>
    /// <param name="configureServices">
    /// Extra service replacements for the flow's Host, for the one flow that injects a failure. They
    /// are applied after the frozen clock, so a flow never replaces the clock by accident.
    /// </param>
    /// <param name="settings">
    /// Extra Host settings, for a flow whose channels need them — the provider's key and a dispatcher
    /// schedule short enough to observe.
    /// </param>
    public FlowContext(
        PostgresFixture postgres,
        Action<IServiceCollection>? configureServices = null,
        IReadOnlyDictionary<string, string>? settings = null)
    {
        _postgres = postgres;
        _configureServices = configureServices;
        _settings = settings;
    }

    /// <summary>Today in the railway's time zone, the service date of every journey the flow builds.</summary>
    public DateOnly Today { get; private set; }

    /// <summary>The frozen instant the Host sees as now, until the flow moves it.</summary>
    public DateTimeOffset Now { get; private set; }

    /// <summary>
    /// The Host's clock. It stays still unless a flow moves it, which is how a scheduled retry is
    /// waited for without sleeping.
    /// </summary>
    public TestClock Clock => _clock!;

    /// <summary>Creates the flows' journeys through the real generation announcement.</summary>
    public JourneyBuilder Journeys { get; private set; } = null!;

    /// <summary>The white-box view of a journey's stored state.</summary>
    public JourneyInternals Internals { get; private set; } = null!;

    /// <summary>Identity's outbox, where a refused write is announced.</summary>
    public IdentityOutbox Identity { get; private set; } = null!;

    /// <summary>The audit record itself.</summary>
    public AuditDriver Audit { get; private set; } = null!;

    /// <summary>The Auditor's read surface.</summary>
    public AuditorDriver Auditor { get; private set; } = null!;

    /// <summary>The anonymous passenger channel.</summary>
    public PublicApiDriver Public { get; private set; } = null!;

    /// <summary>The seeded Guard's surface, for the departures a flow logs.</summary>
    public GuardDriver Guard { get; private set; } = null!;

    /// <summary>The controller writes a flow publishes, arranged as steps.</summary>
    public ChangePublishingSteps Changes { get; private set; } = null!;

    /// <summary>The catering provider's callback subscription and delivery rows.</summary>
    public ProviderDriver Provider { get; private set; } = null!;

    /// <summary>The five controller writes, each as a step a flow can arrange and attempt.</summary>
    public ControllerWriteSteps Writes { get; private set; } = null!;

    /// <summary>The controller's write surface, as other drivers or sessions reach it.</summary>
    public ControllerDriver ControllerWrites(StaffSession? session) => new(Client, session);

    /// <summary>
    /// The flow's own HTTP client, for the drivers a test builds itself — a Guard with another
    /// session, or a call made without a token.
    /// </summary>
    public HttpClient Api => Client;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        Today = RailwayDay.Today();
        Now = RailwayDay.Noon(Today);
        _clock = new TestClock(Now);

        _factory = new SmartRailApiFactory(
            _postgres.ConnectionString,
            configureServices: services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Clock);
                _configureServices?.Invoke(services);
            },
            settings: WithStreamHeartbeat(_settings));

        _client = _factory.CreateApiClient();
        _identityBaseline = await new IdentityOutbox(_postgres).MaxPositionAsync();

        using var ready = await _client.GetAsync("/health/ready");

        Verify.Equal(
            HttpStatusCode.OK,
            ready.StatusCode,
            "The Host should be ready, with its schema migrated, before a flow starts");

        Internals = new JourneyInternals(_postgres);
        Identity = new IdentityOutbox(_postgres);
        Audit = new AuditDriver(_postgres);
        Auditor = new AuditorDriver(_client, StaffDirectory.Auditor);
        Public = new PublicApiDriver(_client);
        Guard = new GuardDriver(_client, StaffDirectory.Guard);
        Journeys = new JourneyBuilder(_factory, _postgres, Today, Now);
        Changes = new ChangePublishingSteps(_client, Now);
        Provider = new ProviderDriver(_client, _postgres);
        Writes = new ControllerWriteSteps(_client, Journeys, Now);

        _auditBaseline = await Audit.MaxChainPositionAsync();
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

        foreach (var journey in Journeys.Created)
        {
            await Internals.CleanUpAsync(journey.Id);
        }

        // A subscription would keep calling the stub receiver after the flow's Host is gone.
        await Provider.CleanUpAsync();

        // The announcements go after their deliveries finished, so no entry lands while the record is
        // being returned to the state the flow found it in.
        await Identity.CleanUpAfterAsync(_identityBaseline);
        await Audit.ClearAfterAsync(_auditBaseline);

        _client.Dispose();
        _factory.Dispose();
    }

    private HttpClient Client =>
        _client ?? throw new InvalidOperationException("The flow has not been initialised.");
}
