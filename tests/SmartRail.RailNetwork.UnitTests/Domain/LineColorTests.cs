using SmartRail.RailNetwork.Domain.Entities;
using SmartRail.RailNetwork.Domain.ValueObjects;

namespace SmartRail.RailNetwork.UnitTests.Domain;

/// <summary>The line colour is a value object with one format, so the rule lives in the domain.</summary>
public sealed class LineColorTests
{
    [Fact]
    public void A_valid_hex_colour_is_accepted()
    {
        var result = LineColor.Create("#1d4ed8");

        Assert.True(result.IsSuccess);
        Assert.Equal("#1d4ed8", result.Value.Value);
    }

    [Fact]
    public void An_uppercase_colour_is_normalised_to_lowercase()
    {
        var result = LineColor.Create("#1D4ED8");

        Assert.True(result.IsSuccess);
        Assert.Equal("#1d4ed8", result.Value.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#1d4ed")]
    [InlineData("#1d4ed88")]
    [InlineData("1d4ed8")]
    [InlineData("#1d4g78")]
    [InlineData("blue")]
    public void A_value_that_is_not_a_hex_colour_is_rejected(string? value)
    {
        var result = LineColor.Create(value);

        Assert.True(result.IsFailure);
        Assert.Equal(
            string.IsNullOrWhiteSpace(value) ? LineErrors.ColorRequired : LineErrors.ColorInvalid,
            result.Error);
    }
}
