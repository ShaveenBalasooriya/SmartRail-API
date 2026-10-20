using SmartRail.SharedKernel.Primitives;
using SmartRail.SharedKernel.Results;

namespace SmartRail.RailNetwork.Domain.ValueObjects;

/// <summary>A line's short code. It is unique across the network.</summary>
public sealed class LineCode : ValueObject
{
    private static readonly Error Required = Error.Validation("line.code", "A line code is required.");

    private LineCode(string value) => Value = value;

    /// <summary>The code as it is stored and shown.</summary>
    public string Value { get; }

    /// <summary>Creates a line code, rejecting a blank value.</summary>
    public static Result<LineCode> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Failure<LineCode>(Required);
        }

        return new LineCode(value.Trim());
    }

    /// <summary>
    /// Rebuilds a code from the value EF Core materialises. The stored value was validated when the
    /// row was written, so this does not validate again.
    /// </summary>
    public static LineCode FromStorage(string value) => new(value);

    /// <inheritdoc />
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
