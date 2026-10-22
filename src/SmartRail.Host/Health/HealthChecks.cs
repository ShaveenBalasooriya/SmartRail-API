using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SmartRail.Host.Health;

/// <summary>
/// The readiness checks: the database can be reached, every module's migrations are applied and
/// start-up seeding has finished (service-health spec).
/// </summary>
public static class HealthChecks
{
    /// <summary>The liveness check's tag.</summary>
    public const string LiveTag = "live";

    /// <summary>The readiness check's tag.</summary>
    public const string ReadyTag = "ready";

    /// <summary>The database check's registration name, which is what a failure body reports.</summary>
    public const string DatabaseCheckName = "database";

    /// <summary>The migrations check's registration name.</summary>
    public const string MigrationsCheckName = "migrations";

    /// <summary>The seeding check's registration name.</summary>
    public const string SeedingCheckName = "seeding";
}

/// <summary>
/// Checks that the database answers a trivial query. It uses the Host's connection string directly
/// rather than a module's context, because readiness is about the database itself.
/// </summary>
public sealed class DatabaseHealthCheck(Npgsql.NpgsqlDataSource dataSource) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var command = dataSource.CreateCommand("SELECT 1");
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The exception text never reaches the response body: only the check's name and status
            // do (service-health spec). It is available to the operator in the log.
            return HealthCheckResult.Unhealthy(description: null, exception: exception);
        }
    }
}

/// <summary>
/// Checks that start-up seeding has finished. Reference data that is missing or half-loaded is not a
/// ready application, so until the Host has seeded every module, it is not ready.
/// </summary>
public sealed class SeedingCompletedHealthCheck(Persistence.DatabaseSeeder seeder) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(seeder.HasCompleted
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy());
    }
}

/// <summary>
/// Checks that no module has a pending migration. A database that is reachable but behind the code
/// cannot serve requests correctly, so it is not ready.
/// </summary>
public sealed class MigrationsAppliedHealthCheck(Persistence.DatabaseMigrator migrator) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var applied = await migrator.AreMigrationsAppliedAsync(cancellationToken);
            return applied
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy(description: null, exception: exception);
        }
    }
}
