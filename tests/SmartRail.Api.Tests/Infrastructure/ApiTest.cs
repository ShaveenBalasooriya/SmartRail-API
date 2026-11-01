namespace SmartRail.Api.Tests.Infrastructure;

/// <summary>
/// The base class of every API test: one target per test, created before the test and disposed
/// after it, so each test starts against the seeded railway and owns every id it touches.
/// </summary>
/// <param name="database">The run's container (or nothing, when the target is remote).</param>
public abstract class ApiTest(ApiDatabaseFixture database) : IAsyncLifetime
{
    /// <summary>The API this test talks to, in-process or deployed.</summary>
    protected ApiTarget Api { get; private set; } = null!;

    /// <summary>Creates this test's target.</summary>
    public Task InitializeAsync()
    {
        Api = ApiTarget.Start(database.Container);
        return Task.CompletedTask;
    }

    /// <summary>Releases the target and the database it created.</summary>
    public async Task DisposeAsync() => await Api.DisposeAsync();
}

/// <summary>
/// A fact that is skipped, with the reason, when the configured target lacks a prerequisite. Every
/// test here uses it, so a half-configured remote run skips loudly instead of failing obscurely.
/// </summary>
public sealed class ApiFactAttribute : FactAttribute
{
    /// <summary>Marks the test skipped when the target cannot run it.</summary>
    public ApiFactAttribute()
    {
        if (ApiTarget.MissingPrerequisite is { } missing)
        {
            Skip = missing;
        }
    }
}

/// <summary>The theory form of <see cref="ApiFactAttribute"/>.</summary>
public sealed class ApiTheoryAttribute : TheoryAttribute
{
    /// <summary>Marks the theory skipped when the target cannot run it.</summary>
    public ApiTheoryAttribute()
    {
        if (ApiTarget.MissingPrerequisite is { } missing)
        {
            Skip = missing;
        }
    }
}

/// <summary>
/// A fact that needs the in-process host's own signing key (for example to mint an expired token),
/// so it is skipped, with the reason, against a deployed API whose key the tests do not hold.
/// </summary>
public sealed class InProcessApiFactAttribute : FactAttribute
{
    /// <summary>Marks the test skipped when the target is remote or lacks a prerequisite.</summary>
    public InProcessApiFactAttribute()
    {
        if (ApiTarget.IsRemote)
        {
            Skip = "Needs the in-process host's signing key to mint the token; not available against a deployed API.";
        }
        else if (ApiTarget.MissingPrerequisite is { } missing)
        {
            Skip = missing;
        }
    }
}
