using Microsoft.Extensions.DependencyInjection;
using SmartRail.SharedKernel.Application.Abstractions;

namespace SmartRail.Host.Persistence;

/// <summary>
/// Runs every module's seeder once, after all migrations and before the application serves traffic
/// (design D9). A failure stops start-up, because serving from a half-seeded database is worse than
/// not starting.
/// </summary>
/// <remarks>
/// Each seeder receives a scoped provider because it writes through its module's <c>DbContext</c>.
/// They run in <see cref="DatabaseMigrator.SchemaOrder"/>, so a module that references another
/// module's seeded data finds it in place.
/// </remarks>
public sealed class DatabaseSeeder(
    IServiceProvider services,
    ILogger<DatabaseSeeder> logger)
{
    private volatile bool _completed;

    /// <summary>
    /// True once every seeder has finished. Readiness reports unhealthy until it is, so a probe
    /// cannot call the application ready before its reference data exists.
    /// </summary>
    public bool HasCompleted => _completed;

    /// <summary>Runs every registered seeder in schema order.</summary>
    /// <exception cref="InvalidOperationException">A module's seeding failed.</exception>
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();

        var seeders = scope.ServiceProvider.GetServices<IModuleSeeder>()
            .OrderBy(seeder => OrderOf(seeder.Schema))
            .ToList();

        foreach (var seeder in seeders)
        {
            try
            {
                await seeder.SeedAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogCritical(
                    exception,
                    "Seeding for module {Module} (schema {Schema}) failed; start-up is stopped",
                    seeder.Schema,
                    seeder.Schema);

                throw new InvalidOperationException(
                    $"Seeding for module '{seeder.Schema}' failed.",
                    exception);
            }

            logger.LogInformation("Seeding applied for schema {Schema}", seeder.Schema);
        }

        _completed = true;
    }

    private static int OrderOf(string schema)
    {
        var index = Array.IndexOf(DatabaseMigrator.SchemaOrder, schema);

        // A seeder whose schema is not in the start-up order runs last rather than first.
        return index < 0 ? int.MaxValue : index;
    }
}
