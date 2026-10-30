using System.Diagnostics.CodeAnalysis;
using SmartRail.SharedKernel.Results;

namespace SmartRail.LiveOperations.Domain.Departures;

/// <summary>
/// Converts between <see cref="DelayCause"/> and the text stored in
/// <c>operations.departure_logs.delay_cause</c>. The list is the contract the Guard app sends, so a
/// value outside it is a validation failure rather than an exception (departure-logging spec,
/// "Delay cause from a fixed list").
/// </summary>
public static class DelayCauses
{
    private static readonly Dictionary<string, DelayCause> ByStorage = new(StringComparer.Ordinal)
    {
        ["signalling"] = DelayCause.Signalling,
        ["rolling_stock"] = DelayCause.RollingStock,
        ["crew"] = DelayCause.Crew,
        ["passenger_boarding"] = DelayCause.PassengerBoarding,
        ["infrastructure"] = DelayCause.Infrastructure,
        ["weather"] = DelayCause.Weather,
        ["other"] = DelayCause.Other,
    };

    /// <summary>
    /// The cause the text names, or a validation failure when the text is not one of the seven
    /// values. The text is matched exactly, so a cause the contract does not name is refused rather
    /// than inferred.
    /// </summary>
    public static Result<DelayCause> Parse(string? value)
    {
        return value is not null && ByStorage.TryGetValue(value, out var cause)
            ? Result.Success(cause)
            : Result.Failure<DelayCause>(DepartureLogErrors.UnknownDelayCause(value));
    }

    /// <summary>The text stored for a cause.</summary>
    public static string ToStorage(DelayCause cause) => cause switch
    {
        DelayCause.Signalling => "signalling",
        DelayCause.RollingStock => "rolling_stock",
        DelayCause.Crew => "crew",
        DelayCause.PassengerBoarding => "passenger_boarding",
        DelayCause.Infrastructure => "infrastructure",
        DelayCause.Weather => "weather",
        DelayCause.Other => "other",
        _ => throw new ArgumentOutOfRangeException(nameof(cause), cause, "Unknown delay cause."),
    };

    /// <summary>The cause a stored text names.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The text is not a known cause, which means the row predates or bypassed the check constraint.</exception>
    public static DelayCause FromStorage(string value)
    {
        return TryFromStorage(value, out var cause)
            ? cause.Value
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown delay cause.");
    }

    /// <summary>The cause a stored text names, when it is one of the seven values.</summary>
    public static bool TryFromStorage(string? value, [NotNullWhen(true)] out DelayCause? cause)
    {
        if (value is not null && ByStorage.TryGetValue(value, out var parsed))
        {
            cause = parsed;
            return true;
        }

        cause = null;
        return false;
    }
}
