using System.Text.Json;
using SmartRail.SharedKernel.Primitives;
using SmartRail.SharedKernel.Results;

namespace SmartRail.Audit.Domain.Entries;

/// <summary>
/// One entry of the append-only audit record (data model <c>audit.entries</c>). An entry records a
/// staff member's successful state change or a denied write attempt, and carries its position in
/// the single global hash chain and the links (<see cref="PreviousHash"/>, <see cref="EntryHash"/>)
/// that make the chain tamper-evident (design D2, D3).
/// </summary>
/// <remarks>
/// <see cref="SourceEventId"/> is not part of the hash: it identifies the bus event that produced a
/// denial, so that a redelivered event adds no second entry (design D5).
/// </remarks>
public sealed class AuditEntry : Entity<Guid>
{
    private AuditEntry()
    {
    }

    private AuditEntry(
        Guid id,
        long chainPosition,
        DateTimeOffset occurredAt,
        Guid? actorStaffId,
        string? actorRole,
        string action,
        AuditOutcome outcome,
        string? targetType,
        string? targetId,
        string? before,
        string? after,
        Guid? relatedEntryId,
        Guid? sourceEventId,
        string previousHash)
        : base(id)
    {
        ChainPosition = chainPosition;
        OccurredAt = occurredAt;
        ActorStaffId = actorStaffId;
        ActorRole = actorRole;
        Action = action;
        Outcome = outcome;
        TargetType = targetType;
        TargetId = targetId;
        Before = before;
        After = after;
        RelatedEntryId = relatedEntryId;
        SourceEventId = sourceEventId;
        PreviousHash = previousHash;
    }

    /// <summary>The entry's gap-free position in the single global chain, starting at 1.</summary>
    public long ChainPosition { get; private set; }

    /// <summary>The server time the action occurred, in UTC and truncated to microseconds.</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>The staff member who acted, or <c>null</c> when a denial's actor is not known.</summary>
    public Guid? ActorStaffId { get; private set; }

    /// <summary>The role the staff member acted in at the time, or <c>null</c> when it is not known.</summary>
    public string? ActorRole { get; private set; }

    /// <summary>The name of the action that succeeded or was attempted.</summary>
    public string Action { get; private set; } = null!;

    public AuditOutcome Outcome { get; private set; }

    /// <summary>The kind of thing the action changed; <c>null</c> for a denial.</summary>
    public string? TargetType { get; private set; }

    /// <summary>The identifier of the thing the action changed; <c>null</c> for a denial.</summary>
    public string? TargetId { get; private set; }

    /// <summary>The value before the change as JSON, or <c>null</c> when there was none.</summary>
    public string? Before { get; private set; }

    /// <summary>The value after the change as JSON, or <c>null</c> when there was none.</summary>
    public string? After { get; private set; }

    /// <summary>The entry of the change this entry corrects, when it is a correction.</summary>
    public Guid? RelatedEntryId { get; private set; }

    /// <summary>The bus event that produced a denial entry; <c>null</c> for a staff state change.</summary>
    public Guid? SourceEventId { get; private set; }

    /// <summary>The hash of the entry before this one, or 64 zeros for the first entry.</summary>
    public string PreviousHash { get; private set; } = null!;

    /// <summary>The entry's own hash, which the next entry links to.</summary>
    public string EntryHash { get; private set; } = null!;

    /// <summary>
    /// Creates an entry and computes its hash (design D3), normalising the time to UTC and
    /// truncating it to the microsecond precision PostgreSQL stores.
    /// </summary>
    public static Result<AuditEntry> Create(
        Guid id,
        long chainPosition,
        DateTimeOffset occurredAt,
        Guid? actorStaffId,
        string? actorRole,
        string action,
        AuditOutcome outcome,
        string? targetType,
        string? targetId,
        string? before,
        string? after,
        Guid? relatedEntryId,
        Guid? sourceEventId,
        string previousHash)
    {
        if (id == Guid.Empty)
        {
            return Result.Failure<AuditEntry>(AuditEntryErrors.IdRequired);
        }

        if (chainPosition < 1)
        {
            return Result.Failure<AuditEntry>(AuditEntryErrors.ChainPositionRequired);
        }

        if (occurredAt == default)
        {
            return Result.Failure<AuditEntry>(AuditEntryErrors.OccurredAtRequired);
        }

        if (string.IsNullOrWhiteSpace(action))
        {
            return Result.Failure<AuditEntry>(AuditEntryErrors.ActionRequired);
        }

        if (actorRole is not null && string.IsNullOrWhiteSpace(actorRole))
        {
            return Result.Failure<AuditEntry>(AuditEntryErrors.ActorRoleBlank);
        }

        if (targetType is not null && string.IsNullOrWhiteSpace(targetType))
        {
            return Result.Failure<AuditEntry>(AuditEntryErrors.TargetTypeBlank);
        }

        if (targetId is not null && string.IsNullOrWhiteSpace(targetId))
        {
            return Result.Failure<AuditEntry>(AuditEntryErrors.TargetIdBlank);
        }

        if (outcome == AuditOutcome.Succeeded)
        {
            if (actorStaffId is null || actorStaffId == Guid.Empty)
            {
                return Result.Failure<AuditEntry>(AuditEntryErrors.ActorStaffIdRequired);
            }

            if (actorRole is null)
            {
                return Result.Failure<AuditEntry>(AuditEntryErrors.ActorRoleRequired);
            }

            if (targetType is null)
            {
                return Result.Failure<AuditEntry>(AuditEntryErrors.TargetTypeRequired);
            }

            if (targetId is null)
            {
                return Result.Failure<AuditEntry>(AuditEntryErrors.TargetIdRequired);
            }
        }

        if (!IsValidJson(before))
        {
            return Result.Failure<AuditEntry>(AuditEntryErrors.BeforeInvalidJson);
        }

        if (!IsValidJson(after))
        {
            return Result.Failure<AuditEntry>(AuditEntryErrors.AfterInvalidJson);
        }

        if (!AuditEntryHash.IsWellFormed(previousHash))
        {
            return Result.Failure<AuditEntry>(AuditEntryErrors.PreviousHashInvalid);
        }

        var entry = new AuditEntry(
            id,
            chainPosition,
            TruncateToMicroseconds(occurredAt),
            actorStaffId == Guid.Empty ? null : actorStaffId,
            actorRole?.Trim(),
            action.Trim(),
            outcome,
            targetType?.Trim(),
            targetId?.Trim(),
            before,
            after,
            relatedEntryId,
            sourceEventId,
            previousHash);

        entry.EntryHash = AuditEntryHash.Compute(entry);

        return entry;
    }

    /// <summary>
    /// Recomputes the hash from the entry's stored content and <see cref="PreviousHash"/>, which is
    /// how the integrity check detects a row that was altered directly in the database.
    /// </summary>
    public string RecomputeHash() => AuditEntryHash.Compute(this);

    private static bool IsValidJson(string? json)
    {
        if (json is null)
        {
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static DateTimeOffset TruncateToMicroseconds(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        var microseconds = utc.Ticks - (utc.Ticks % TimeSpan.TicksPerMicrosecond);

        return new DateTimeOffset(microseconds, TimeSpan.Zero);
    }
}
