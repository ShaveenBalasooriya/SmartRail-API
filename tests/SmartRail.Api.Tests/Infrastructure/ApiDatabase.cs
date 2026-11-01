using SmartRail.Host.IntegrationTests.Infrastructure;

namespace SmartRail.Api.Tests.Infrastructure;

/// <summary>
/// The one PostgreSQL container the API tests share. Every test still gets its own database on it
/// (see <see cref="ApiTarget"/>), so the per-test cost is a database, not a container.
/// </summary>
public sealed class ApiDatabaseFixture : IAsyncLifetime
{
    private PostgresFixture? _container;

    /// <summary>The container, or <see langword="null"/> when the tests target a deployed API.</summary>
    public PostgresFixture? Container => _container;

    /// <summary>Starts the container unless the target is remote, which needs no Docker at all.</summary>
    public async Task InitializeAsync()
    {
        if (ApiTarget.IsRemote)
        {
            return;
        }

        _container = new PostgresFixture();
        await _container.InitializeAsync();
    }

    /// <summary>Stops the container.</summary>
    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}

/// <summary>
/// The collection every API test joins. One collection means one container and one test at a time,
/// which is what keeps a run's cost predictable and its failures readable.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ApiDatabase : ICollectionFixture<ApiDatabaseFixture>
{
    /// <summary>The collection name every test class in this project carries.</summary>
    public const string Name = "api-database";
}
