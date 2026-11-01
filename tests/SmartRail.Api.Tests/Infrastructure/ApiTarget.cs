using Npgsql;
using SmartRail.Host.IntegrationTests.Infrastructure;

namespace SmartRail.Api.Tests.Infrastructure;

/// <summary>
/// The one place that knows which API the tests talk to, so every test is written once and can run
/// either way (test-suite-review §4 "Switching environments").
/// </summary>
/// <remarks>
/// <para>
/// <c>SMARTRAIL_TEST_TARGET</c> picks the target. Unset, empty or <c>inprocess</c> starts the real
/// Host in this process with <see cref="SmartRailApiFactory"/>, against its own database on the
/// shared PostgreSQL container, so every test starts from the seeded railway with no other test's
/// writes in it.
/// </para>
/// <para>
/// <c>remote</c> sends the same tests to a deployed API. The deployed environment must then supply
/// these variables, which the tests never default, because a test that silently used the wrong
/// environment would be worse than a skipped one:
/// </para>
/// <list type="table">
/// <item><term><c>SMARTRAIL_API_URL</c></term><description>Base URL, for example <c>https://staging.example</c>.</description></item>
/// <item><term><c>SMARTRAIL_CONTROLLER_TOKEN</c></term><description>A Network Controller bearer token.</description></item>
/// <item><term><c>SMARTRAIL_GUARD_TOKEN</c></term><description>A Guard bearer token.</description></item>
/// <item><term><c>SMARTRAIL_AUDITOR_TOKEN</c></term><description>An Auditor bearer token.</description></item>
/// <item><term><c>SMARTRAIL_PROVIDER_KEY</c></term><description>The catering provider's API key (<c>X-Api-Key</c>).</description></item>
/// </list>
/// <para>
/// The tests need no seeded identifiers: every journey, stop, platform and change they use is read
/// back through the API, and the ids they assert on are the ones the API returned to them. A remote
/// environment therefore has to be freshly seeded and disposable, because the writes are real.
/// </para>
/// </remarks>
public sealed class ApiTarget : IAsyncDisposable
{
    /// <summary>Picks the target: <c>inprocess</c> (the default) or <c>remote</c>.</summary>
    public const string TargetVariable = "SMARTRAIL_TEST_TARGET";

    /// <summary>The base URL of a deployed API, used when the target is <c>remote</c>.</summary>
    public const string BaseUrlVariable = "SMARTRAIL_API_URL";

    /// <summary>The Network Controller's bearer token, used when the target is <c>remote</c>.</summary>
    public const string ControllerTokenVariable = "SMARTRAIL_CONTROLLER_TOKEN";

    /// <summary>The Guard's bearer token, used when the target is <c>remote</c>.</summary>
    public const string GuardTokenVariable = "SMARTRAIL_GUARD_TOKEN";

    /// <summary>The Auditor's bearer token, used when the target is <c>remote</c>.</summary>
    public const string AuditorTokenVariable = "SMARTRAIL_AUDITOR_TOKEN";

    /// <summary>The catering provider's API key, used when the target is <c>remote</c>.</summary>
    public const string ProviderKeyVariable = "SMARTRAIL_PROVIDER_KEY";

    /// <summary>The setting the Identity seeder reads the plaintext provider key from.</summary>
    private const string ProviderKeySetting = "Catering:ProviderApiKey";

    /// <summary>
    /// The key every in-process test is seeded with. It is not a secret: the container is disposable
    /// and the value never leaves the test process.
    /// </summary>
    private const string InProcessProviderKey = "srk_apitest9_2f8c1d7e9b1a4c6d3e0f7a2b8c9d1e4f";

    private readonly SmartRailApiFactory? _host;
    private readonly PostgresFixture? _container;
    private readonly string? _databaseName;

    private ApiTarget(
        HttpClient client,
        ApiCredentials credentials,
        SmartRailApiFactory? host,
        PostgresFixture? container,
        string? databaseName)
    {
        Client = client;
        Credentials = credentials;
        _host = host;
        _container = container;
        _databaseName = databaseName;
    }

    /// <summary>True when the tests call a deployed API instead of starting the Host themselves.</summary>
    public static bool IsRemote { get; } =
        string.Equals(Environment.GetEnvironmentVariable(TargetVariable), "remote", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Why a test cannot run against the configured target, or <see langword="null"/> when it can.
    /// Only a remote target can be incomplete; an in-process one always has its container.
    /// </summary>
    public static string? MissingPrerequisite { get; } = MissingRemotePrerequisite();

    /// <summary>The client every request goes through. Redirects are off, so a status code is what it says.</summary>
    public HttpClient Client { get; }

    /// <summary>The credentials the tests act with, one per role and one for the provider.</summary>
    public ApiCredentials Credentials { get; }

    /// <summary>Authenticates a request as the Network Controller.</summary>
    public Credential Controller => Credential.Staff(Credentials.Controller);

    /// <summary>Authenticates a request as the Guard.</summary>
    public Credential Guard => Credential.Staff(Credentials.Guard);

    /// <summary>Authenticates a request as the Auditor.</summary>
    public Credential Auditor => Credential.Staff(Credentials.Auditor);

    /// <summary>Authenticates a request as the catering provider.</summary>
    public Credential Provider => Credential.Provider(Credentials.ProviderKey);

    /// <summary>Starts the target this test run is configured for.</summary>
    /// <param name="container">
    /// The run's PostgreSQL container; it is only needed by the in-process target, which creates its
    /// own database on it.
    /// </param>
    public static ApiTarget Start(PostgresFixture? container)
    {
        if (IsRemote)
        {
            return StartRemote();
        }

        var configured = Environment.GetEnvironmentVariable(TargetVariable);
        if (!string.IsNullOrWhiteSpace(configured)
            && !string.Equals(configured, "inprocess", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{TargetVariable} is '{configured}'. Use 'inprocess' (the default) or 'remote'.");
        }

        if (container is null)
        {
            throw new InvalidOperationException(
                "The in-process target needs the PostgreSQL container; the test collection fixture did not start one.");
        }

        // A database of its own per test: the Host migrates and seeds it on start-up, so a test sees
        // the seeded railway and nothing another test wrote.
        var databaseName = $"smartrail_api_{Guid.NewGuid():N}";
        var host = new SmartRailApiFactory(
            WithDatabase(container.ConnectionString, databaseName),
            settings: new Dictionary<string, string> { [ProviderKeySetting] = InProcessProviderKey });

        var credentials = new ApiCredentials(
            Controller: TestTokens.Create("controller.mahesh", "network_controller"),
            Guard: TestTokens.Create("guard.ishara", "guard"),
            Auditor: TestTokens.Create("auditor.wasantha", "auditor"),
            ProviderKey: InProcessProviderKey);

        return new ApiTarget(host.CreateApiClient(), credentials, host, container, databaseName);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        _host?.Dispose();

        // The container is thrown away with the run, but dropping the database keeps a long run
        // from filling it; FORCE closes the pool's idle connections first.
        if (_container is not null && _databaseName is not null)
        {
            await _container.ExecuteAsync($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)");
        }
    }

    private static ApiTarget StartRemote()
    {
        var baseUrl = Required(BaseUrlVariable);
        var client = new HttpClient { BaseAddress = new Uri(baseUrl, UriKind.Absolute) };

        var credentials = new ApiCredentials(
            Required(ControllerTokenVariable),
            Required(GuardTokenVariable),
            Required(AuditorTokenVariable),
            Required(ProviderKeyVariable));

        return new ApiTarget(client, credentials, host: null, container: null, databaseName: null);
    }

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException(
                $"{name} is not set. Every remote test is skipped until it is; see ApiTarget for the list.");

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
            ? null
            : $"Remote target: set {string.Join(", ", missing)} to run this test against a deployed API.";
    }

    private static string WithDatabase(string connectionString, string databaseName) =>
        new NpgsqlConnectionStringBuilder(connectionString) { Database = databaseName }.ConnectionString;
}

/// <summary>The credential each role authenticates with, so a test never mints or names one itself.</summary>
/// <param name="Controller">The Network Controller's bearer token.</param>
/// <param name="Guard">The Guard's bearer token.</param>
/// <param name="Auditor">The Auditor's bearer token.</param>
/// <param name="ProviderKey">The catering provider's API key.</param>
public sealed record ApiCredentials(string Controller, string Guard, string Auditor, string ProviderKey);
