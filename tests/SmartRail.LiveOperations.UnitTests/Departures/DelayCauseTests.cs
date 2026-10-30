using SmartRail.LiveOperations.Domain.Departures;
using SmartRail.SharedKernel.Results;

namespace SmartRail.LiveOperations.UnitTests.Departures;

/// <summary>
/// The fixed delay-cause list (GRD-03, departure-logging spec "Delay cause from a fixed list"): the
/// seven values the Guard app may send parse, and anything else is a validation failure so the
/// endpoint answers HTTP 400 and the app stops retrying it.
/// </summary>
public sealed class DelayCauseTests
{
    [Theory]
    [InlineData("signalling", DelayCause.Signalling)]
    [InlineData("rolling_stock", DelayCause.RollingStock)]
    [InlineData("crew", DelayCause.Crew)]
    [InlineData("passenger_boarding", DelayCause.PassengerBoarding)]
    [InlineData("infrastructure", DelayCause.Infrastructure)]
    [InlineData("weather", DelayCause.Weather)]
    [InlineData("other", DelayCause.Other)]
    public void Every_value_of_the_fixed_list_parses(string text, DelayCause expected)
    {
        var result = DelayCauses.Parse(text);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value);
    }

    [Theory]
    [InlineData("accident")]
    [InlineData("Signalling")]
    [InlineData("rolling-stock")]
    [InlineData("")]
    public void A_value_outside_the_fixed_list_is_refused(string text)
    {
        var result = DelayCauses.Parse(text);

        Assert.True(result.IsFailure);
        Assert.Equal("DepartureLog.UnknownDelayCause", result.Error.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    [Fact]
    public void An_absent_cause_is_not_a_cause()
    {
        var result = DelayCauses.Parse(null);

        Assert.True(result.IsFailure);
        Assert.Equal(DepartureLogErrors.UnknownDelayCause(null), result.Error);
    }

    [Theory]
    [InlineData(DelayCause.Signalling, "signalling")]
    [InlineData(DelayCause.RollingStock, "rolling_stock")]
    [InlineData(DelayCause.Crew, "crew")]
    [InlineData(DelayCause.PassengerBoarding, "passenger_boarding")]
    [InlineData(DelayCause.Infrastructure, "infrastructure")]
    [InlineData(DelayCause.Weather, "weather")]
    [InlineData(DelayCause.Other, "other")]
    public void Each_cause_round_trips_through_its_stored_text(DelayCause cause, string text)
    {
        Assert.Equal(text, DelayCauses.ToStorage(cause));
        Assert.Equal(cause, DelayCauses.FromStorage(text));
    }
}
