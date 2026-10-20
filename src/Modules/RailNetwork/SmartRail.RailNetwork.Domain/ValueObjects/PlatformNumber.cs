using SmartRail.SharedKernel.Primitives;
using SmartRail.SharedKernel.Results;

namespace SmartRail.RailNetwork.Domain.ValueObjects;

/// <summary>A platform's number or letter. It is unique within its station.</summary>
public sealed class PlatformNumber : ValueObject
{
    private static readonly Error Required =
        Error.Validation("platform.number", "A platform number is required.");

    private PlatformNumber(string value) => Value = value;

    /// <summary>The number as it is stored and shown.</summary>
    public string Value { get; }

    /// <summary>Creates a platform number, rejecting a blank value.</summary>
    public static Result<PlatformNumber> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Failure<PlatformNumber>(Required);
        }

        return new PlatformNumber(value.Trim());
    }

    /// <summary>
    /// Rebuilds a number from the value EF Core materialises. The stored value was validated when
    /// the row was written, so this does not validate again.
    /// </summary>
    public static PlatformNumber FromStorage(string value) => new(value);

    /// <inheritdoc />
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
