using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SmartRail.Catering.Domain.Providers;
using SmartRail.Catering.Infrastructure.Persistence;
using SmartRail.SharedKernel.Application.Abstractions;

namespace SmartRail.Catering.Infrastructure.Seeding;

/// <summary>
/// Seeds the catering provider from <c>seed/catering/providers.json</c> (design D9). It is idempotent
/// by the seed's provider identifier, so running it on every start inserts the provider once and
/// changes nothing that exists.
/// </summary>
/// <remarks>
/// The file names the same provider identifier as <c>seed/identity/provider-key.json</c>: Identity owns
/// the key's prefix and hash, Catering owns the provider record they belong to. The plaintext key is
/// configuration (<c>Catering:ProviderApiKey</c>), never a seed file, so no key material is stored here.
/// The whole file is validated before anything is added.
/// </remarks>
internal sealed class CateringProviderSeeder(
    CateringDbContext database,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<CateringProviderSeeder> logger) : IModuleSeeder
{
    /// <summary>The provider seed file, relative to the seed root.</summary>
    public const string ProvidersSeedFile = "catering/providers.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        // An unexpected property is a mistake in the file, not something to ignore silently.
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public string Schema => CateringDbContext.Schema;

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var seedRoot = ResolveSeedRoot(configuration);
        var now = timeProvider.GetUtcNow();

        var providers = ReadProviders(Path.Combine(seedRoot, ProvidersSeedFile), now);

        var existingIds = (await database.Providers
                .Select(provider => provider.Id)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var newProviders = providers
            .Where(provider => !existingIds.Contains(provider.Id))
            .ToList();

        database.Providers.AddRange(newProviders);

        await database.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Seeded schema {Schema}: {ProviderCount} provider(s) added",
            Schema,
            newProviders.Count);
    }

    private static List<Provider> ReadProviders(string path, DateTimeOffset now)
    {
        var records = ReadJson<ProviderSeedRecord[]>(path)
            ?? throw new InvalidOperationException($"Seed file '{path}' contains no records.");
        var providers = new List<Provider>(records.Length);

        for (var index = 0; index < records.Length; index++)
        {
            var record = records[index];
            var result = Provider.Create(record.Id, record.Name, record.IsActive, now);

            if (result.IsFailure)
            {
                throw new InvalidOperationException(
                    $"Seed file '{path}' record {index + 1} ('{record.Name}') is invalid: {result.Error.Description}");
            }

            providers.Add(result.Value);
        }

        return providers;
    }

    private static T? ReadJson<T>(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Seed file '{path}' was not found.");
        }

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException($"Seed file '{path}' could not be read.", exception);
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"Seed file '{path}' is not valid seed data: {exception.Message}",
                exception);
        }
    }

    /// <summary>
    /// The seed root is <c>Seed:Path</c> when configured, resolved against the application directory
    /// when relative, and otherwise the <c>seed</c> folder copied next to the binaries.
    /// </summary>
    private static string ResolveSeedRoot(IConfiguration configuration)
    {
        var configured = configuration["Seed:Path"];

        if (string.IsNullOrWhiteSpace(configured))
        {
            return Path.Combine(AppContext.BaseDirectory, "seed");
        }

        return Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(AppContext.BaseDirectory, configured);
    }

    private sealed record ProviderSeedRecord(Guid Id, string Name, bool IsActive);
}
