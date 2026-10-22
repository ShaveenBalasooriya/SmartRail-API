using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore.Storage;

namespace SmartRail.Host.Persistence;

/// <summary>
/// A module's <c>DbContext</c> type, its schema and its position in the start-up order. The Host
/// needs all three and must not know the module's entities, so it is described here by type only.
/// </summary>
/// <param name="Module">The module name, used in logs.</param>
/// <param name="Schema">The PostgreSQL schema the context owns.</param>
/// <param name="ContextType">The context's CLR type.</param>
public sealed record ModuleDatabase(string Module, string Schema, Type ContextType);

/// <summary>
/// Applies every module's migrations at start-up, in the order design D2 fixes, and creates the
/// database if it is missing.
/// </summary>
/// <remarks>
/// A failure stops start-up: the application must not serve traffic from a schema that is behind
/// the code. Migrations run in order <c>identity</c>, <c>network</c>, <c>operations</c>,
/// <c>catering</c>, <c>audit</c>.
/// </remarks>
public sealed class DatabaseMigrator(
    IServiceProvider services,
    IEnumerable<ModuleDatabase> databases,
    ILogger<DatabaseMigrator> logger)
{
    /// <summary>
    /// The start-up order. Identity is first because every later module references a staff member;
    /// Audit is last because it records what the others do.
    /// </summary>
    public static readonly string[] SchemaOrder = ["identity", "network", "operations", "catering", "audit"];

    /// <summary>Applies migrations for every registered module.</summary>
    /// <exception cref="InvalidOperationException">A module's migrations failed.</exception>
    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        foreach (var database in databases.OrderBy(d => Array.IndexOf(SchemaOrder, d.Schema)))
        {
            // A DbContext is scoped, so each module's migration runs in its own scope.
            using var scope = services.CreateScope();
            var context = (DbContext)scope.ServiceProvider.GetRequiredService(database.ContextType);

            try
            {
                // Migrate() creates the database when it is missing, then creates the module's
                // schema and its own migrations history table. The schema has to exist before the
                // history table does, which is why the schema is created first.
                await EnsureSchemaAsync(context, database.Schema, cancellationToken);
                await context.Database.MigrateAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogCritical(
                    exception,
                    "Migrations for module {Module} (schema {Schema}) failed; start-up is stopped",
                    database.Module,
                    database.Schema);

                throw new InvalidOperationException(
                    $"Migrations for module '{database.Module}' (schema '{database.Schema}') failed.",
                    exception);
            }

            logger.LogInformation(
                "Migrations applied for module {Module} (schema {Schema})",
                database.Module,
                database.Schema);
        }
    }

    /// <summary>
    /// Creates the module's schema when it does not exist. EF Core creates the migrations history
    /// table in that schema before it applies anything, so the schema cannot be left to a migration.
    /// </summary>
    private static async Task EnsureSchemaAsync(DbContext context, string schema, CancellationToken cancellationToken)
    {
        var creator = context.GetService<IRelationalDatabaseCreator>();
        if (!await creator.ExistsAsync(cancellationToken))
        {
            await creator.CreateAsync(cancellationToken);
        }

        var connection = context.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;

        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE SCHEMA IF NOT EXISTS \"{schema.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    /// <summary>
    /// True when every module's migrations are applied. This is the readiness check's second
    /// condition: the database can be reached and the schema is current (service-health spec).
    /// </summary>
    public async Task<bool> AreMigrationsAppliedAsync(CancellationToken cancellationToken)
    {
        foreach (var database in databases)
        {
            using var scope = services.CreateScope();
            var context = (DbContext)scope.ServiceProvider.GetRequiredService(database.ContextType);
            var applied = await context.Database.GetAppliedMigrationsAsync(cancellationToken);
            var pending = await context.Database.GetPendingMigrationsAsync(cancellationToken);

            if (pending.Any())
            {
                logger.LogWarning(
                    "Module {Module} has {PendingCount} migration(s) not applied",
                    database.Module,
                    pending.Count());

                return false;
            }

            logger.LogDebug(
                "Module {Module} has {AppliedCount} migration(s) applied",
                database.Module,
                applied.Count());
        }

        return true;
    }
}
