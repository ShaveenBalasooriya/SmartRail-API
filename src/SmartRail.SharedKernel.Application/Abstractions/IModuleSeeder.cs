namespace SmartRail.SharedKernel.Application.Abstractions;

/// <summary>
/// Loads a module's reviewable reference data from files in the repository at start-up (design D9).
/// A module implements it in Infrastructure and the Host runs every registered seeder after all
/// migrations have been applied, in schema order.
/// </summary>
public interface IModuleSeeder
{
    /// <summary>The module's schema name. It decides where the seeder sits in the start-up order.</summary>
    string Schema { get; }

    /// <summary>
    /// Seeds the module's own schema. It is safe to run on every start: a record already present is
    /// left unchanged and only missing records are inserted.
    /// </summary>
    /// <exception cref="InvalidOperationException">A seed file is malformed or breaks a domain rule.</exception>
    Task SeedAsync(CancellationToken cancellationToken);
}
