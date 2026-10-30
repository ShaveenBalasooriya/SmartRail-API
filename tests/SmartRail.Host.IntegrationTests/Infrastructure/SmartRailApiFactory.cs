using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using SmartRail.Host.Http;
using SmartRail.IdentityAccess.Api.Authentication;
using Testcontainers.PostgreSql;

namespace SmartRail.Host.IntegrationTests.Infrastructure;

/// <summary>
/// One PostgreSQL container for the whole test run. Every integration test starts the real Host
/// against a real database, which is where most of SmartRail's risk sits (architecture §8).
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;

    /// <summary>The connection string of the container, with a short timeout so a stopped database fails fast.</summary>
    public string ConnectionString => $"{_container.GetConnectionString()};Timeout=5;Command Timeout=5";

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder("postgres:17-alpine")
            .WithImage("postgres:17-alpine")
            .WithDatabase("smartrail")
            .WithUsername("smartrail")
            .WithPassword("smartrail")
            .Build();

        await _container.StartAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Runs raw SQL against the container, for assertions the API surface does not expose.</summary>
    public async Task<List<T>> QueryAsync<T>(string sql, Func<Npgsql.NpgsqlDataReader, T> read)
    {
        await using var connection = new Npgsql.NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        var results = new List<T>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(read(reader));
        }

        return results;
    }

    /// <summary>Executes a command and returns the number of rows affected.</summary>
    public async Task<int> ExecuteAsync(string sql)
    {
        await using var connection = new Npgsql.NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteNonQueryAsync();
    }
}

/// <summary>
/// The collection every integration test class joins, so the container starts once.
/// </summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}

/// <summary>
/// Builds the real Host for a test. Configuration is set through <c>UseSetting</c> because the Host
/// reads the connection string while it is still configuring services.
/// </summary>
public sealed class SmartRailApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;
    private readonly string _environment;
    private readonly string? _documentation;
    private readonly Action<IServiceCollection>? _configureServices;
    private readonly IReadOnlyDictionary<string, string>? _settings;

    public SmartRailApiFactory(
        string connectionString,
        string environment = "E2E",
        bool? documentationEnabled = null,
        Action<IServiceCollection>? configureServices = null,
        IReadOnlyDictionary<string, string>? settings = null)
    {
        _connectionString = connectionString;
        _environment = environment;
        _documentation = documentationEnabled?.ToString().ToLowerInvariant();
        _configureServices = configureServices;
        _settings = settings;
    }

    /// <summary>Everything the Host logged while the test ran.</summary>
    public LogCapture Logs { get; } = new();

    /// <summary>An HTTP client with no automatic redirects, so status codes can be asserted.</summary>
    public HttpClient CreateApiClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
    });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment(_environment);
        builder.UseSetting("ConnectionStrings:Database", _connectionString);
        builder.UseSetting("Cors:AllowedOrigins:0", "https://ops.smartrail.test");
        builder.UseSetting("Cors:AllowedOrigins:1", "https://display.smartrail.test");

        // The journey-completion check recomputes every running journey. A test can put a journey
        // into that status with SQL and assert on it undisturbed, so the interval is a day unless a
        // test overrides it, and the job never ticks during the suite.
        builder.UseSetting("Operations:CompletionCheckSeconds", "86400");

        // Staff authentication always has settings, so the Host starts; the test services below
        // then replace the signing key and issuer with a local one (design D11). Development and
        // E2E may use the plain-http local address; every other environment needs https.
        var allowsPlainHttpMetadata =
            string.Equals(_environment, "Development", StringComparison.OrdinalIgnoreCase)
            || string.Equals(_environment, StaffAuthentication.EndToEndEnvironment, StringComparison.OrdinalIgnoreCase);

        builder.UseSetting(
            StaffAuthentication.MetadataAddressSetting,
            allowsPlainHttpMetadata ? TestTokens.MetadataAddress : TestTokens.HttpsMetadataAddress);
        builder.UseSetting(StaffAuthentication.ValidIssuerSetting, TestTokens.Issuer);
        builder.UseSetting(StaffAuthentication.AudienceSetting, TestTokens.Audience);

        // The staff OAuth2 URLs the document publishes when documentation is enabled.
        builder.UseSetting(OpenApiSecurity.StaffAuthorizationUrlSetting, TestTokens.AuthorizationUrl);
        builder.UseSetting(OpenApiSecurity.StaffTokenUrlSetting, TestTokens.TokenUrl);

        if (_documentation is not null)
        {
            builder.UseSetting(OpenApiConfiguration.EnabledSetting, _documentation);
        }

        foreach (var (key, value) in _settings ?? new Dictionary<string, string>())
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            // .NET 10's AddValidation only emits validation metadata for types in the same
            // compilation as the call site. The test-only request types live in this assembly,
            // so the Host's own AddValidation cannot see them; calling it here as well makes the
            // generator resolve them and the validation endpoint returns 400 as the spec requires.
            services.AddValidation();

            // The test-only endpoints live here, not in the Host: the production image has none.
            services.AddSingleton<IEndpointRegistration, TestEndpoints>();
            services.AddSingleton<ILoggerProvider>(Logs);

            // The staff scheme validates tokens signed with the local test key, so no discovery
            // document or Authentik is needed. Setting Configuration stops the handler fetching.
            services.PostConfigure<JwtBearerOptions>(StaffAuthentication.Scheme, options =>
            {
                var signingKey = TestTokens.SigningKeyForTests;

                options.Configuration = new OpenIdConnectConfiguration { Issuer = TestTokens.Issuer };
                options.Configuration.SigningKeys.Add(signingKey);
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = TestTokens.Issuer,
                    ValidateAudience = true,
                    ValidAudience = TestTokens.Audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = signingKey,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = TestTokens.SubjectClaim,
                    AuthenticationType = StaffAuthentication.Scheme,
                };
            });

            _configureServices?.Invoke(services);
        });
    }
}
