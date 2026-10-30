using System.Text.RegularExpressions;
using SmartRail.RailNetwork.Domain.Entities;
using SmartRail.SharedKernel.Primitives;
using SmartRail.SharedKernel.Results;

namespace SmartRail.RailNetwork.Domain.ValueObjects;

/// <summary>
/// A line's display colour as a seven-character hexadecimal string, for example <c>#1d4ed8</c>. It is
/// data the API publishes, so a client never keeps a per-line map of its own (design D5).
/// </summary>
public sealed class LineColor : ValueObject
{
    private static readonly Regex Format = new("^#[0-9a-f]{6}$", RegexOptions.CultureInvariant);

    private LineColor(string value) => Value = value;

    /// <summary>The colour as it is stored and shown: lowercase <c>#rrggbb</c>.</summary>
    public string Value { get; }

    /// <summary>
    /// Creates a line colour, rejecting a blank value and anything that is not <c>#rrggbb</c> after
    /// trimming and lowercasing.
    /// </summary>
    public static Result<LineColor> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Failure<LineColor>(LineErrors.ColorRequired);
        }

        var normalised = value.Trim().ToLowerInvariant();

        if (!Format.IsMatch(normalised))
        {
            return Result.Failure<LineColor>(LineErrors.ColorInvalid);
        }

        return new LineColor(normalised);
    }

    /// <summary>
    /// Rebuilds a colour from the value EF Core materialises. The stored value was validated when the
    /// row was written, so this does not validate again.
    /// </summary>
    public static LineColor FromStorage(string value) => new(value);

    /// <inheritdoc />
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
