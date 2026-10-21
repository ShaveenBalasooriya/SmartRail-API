using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SmartRail.IdentityAccess.Domain.Entities;
using SmartRail.IdentityAccess.Infrastructure.Persistence;
using SmartRail.SharedKernel.Application.Abstractions;

namespace SmartRail.IdentityAccess.Infrastructure.Seeding;

/// <summary>
/// Seeds the staff accounts and the catering provider's API key from <c>seed/identity/</c> (design
/// D9). It is idempotent by natural key — staff by username, the key by prefix — so running it on
/// every start inserts what is missing and changes nothing that exists.
/// </summary>
/// <remarks>
/// The whole file is parsed and validated before anything is added, so a bad record cannot leave
/// half a file committed. The provider's plaintext key comes from configuration and is never logged.
/// </remarks>
internal sealed class IdentitySeeder(
    IdentityDbContext database,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<IdentitySeeder> logger) : IModuleSeeder
{
    /// <summary>The staff seed file, relative to the seed root.</summary>
    public const string StaffSeedFile = "identity/staff.json";

    /// <summary>The provider key seed file, relative to the seed root.</summary>
    public const string ProviderKeySeedFile = "identity/provider-key.json";

    /// <summary>The configuration setting that holds the provider's plaintext key.</summary>
    public const string ProviderKeySetting = "Catering:ProviderApiKey";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        // An unexpected property is a mistake in the file, not something to ignore silently.
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public string Schema => IdentityDbContext.Schema;

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var seedRoot = ResolveSeedRoot(configuration);
        var now = timeProvider.GetUtcNow();

        var staff = ReadStaff(Path.Combine(seedRoot, StaffSeedFile), now);
        var providerKey = ReadProviderKey(Path.Combine(seedRoot, ProviderKeySeedFile), now);

        var existingUsernames = await database.StaffMembers
            .Select(staffMember => staffMember.Username)
            .ToListAsync(cancellationToken);

        var newStaff = staff
            .Where(staffMember => !existingUsernames.Contains(staffMember.Username, StringComparer.Ordinal))
            .ToList();

        database.StaffMembers.AddRange(newStaff);

        var keyAdded = false;
        if (providerKey is not null
            && !await database.ProviderApiKeys.AnyAsync(key => key.KeyPrefix == providerKey.KeyPrefix, cancellationToken))
        {
            database.ProviderApiKeys.Add(providerKey);
            keyAdded = true;
        }

        await database.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Seeded schema {Schema}: {StaffCount} staff member(s) and {KeyCount} provider key(s) added",
            Schema,
            newStaff.Count,
            keyAdded ? 1 : 0);
    }

    private static List<StaffMember> ReadStaff(string path, DateTimeOffset now)
    {
        var records = ReadJson<StaffSeedRecord[]>(path)
            ?? throw new InvalidOperationException($"Seed file '{path}' contains no records.");
        var staff = new List<StaffMember>(records.Length);

        for (var index = 0; index < records.Length; index++)
        {
            var record = records[index];

            // Subject mode is the username, so the seed file needs only the username (design D2).
            var result = StaffMember.Create(record.Id, record.Username, record.Username, record.DisplayName, now);

            if (result.IsFailure)
            {
                throw new InvalidOperationException(
                    $"Seed file '{path}' record {index + 1} ('{record.Username}') is invalid: {result.Error.Description}");
            }

            staff.Add(result.Value);
        }

        return staff;
    }

    private ProviderApiKey? ReadProviderKey(string path, DateTimeOffset now)
    {
        var record = ReadJson<ProviderKeySeedRecord>(path)
            ?? throw new InvalidOperationException($"Seed file '{path}' contains no record.");

        var plaintextKey = configuration[ProviderKeySetting];
        if (string.IsNullOrWhiteSpace(plaintextKey))
        {
            // The key is a secret, so it is not in the seed file: without configuration there is
            // nothing to hash, and the provider simply cannot authenticate until it is supplied.
            logger.LogWarning(
                "{Setting} is not configured; the provider API key was not seeded",
                ProviderKeySetting);

            return null;
        }

        var result = ProviderApiKey.Issue(record.ProviderId, plaintextKey, now);

        if (result.IsFailure)
        {
            throw new InvalidOperationException(
                $"Seed file '{path}' record is invalid: {result.Error.Description}");
        }

        return result.Value;
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

    private sealed record StaffSeedRecord(Guid Id, string Username, string DisplayName);

    private sealed record ProviderKeySeedRecord(Guid ProviderId);
}
