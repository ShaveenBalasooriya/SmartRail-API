using SmartRail.RailNetwork.Application.Network.GetLines;

namespace SmartRail.RailNetwork.UnitTests.Network.GetLines;

/// <summary>
/// The lines query's input rule (design D3): a missing direction means outbound, otherwise only
/// outbound or inbound is accepted, ignoring case, and the error is keyed <c>direction</c>.
/// </summary>
public sealed class GetLinesQueryValidatorTests
{
    private static readonly GetLinesQueryValidator Validator = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("outbound")]
    [InlineData("OUTBOUND")]
    [InlineData("inbound")]
    [InlineData("Inbound")]
    public void Happy_path_accepts_a_known_direction_or_no_direction(string? direction)
    {
        // Arrange
        var query = new GetLinesQuery(direction);

        // Act
        var result = Validator.Validate(query);

        // Assert
        Assert.True(result.IsValid, $"'{direction}' should be an acceptable direction.");
    }

    [Theory]
    [InlineData("sideways")]
    [InlineData("north")]
    [InlineData("1")]
    public void Any_other_direction_is_rejected_on_the_direction_field(string direction)
    {
        // Arrange
        var query = new GetLinesQuery(direction);

        // Act
        var result = Validator.Validate(query);

        // Assert
        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Equal("direction", error.PropertyName);
        Assert.Equal("The direction must be outbound or inbound.", error.ErrorMessage);
    }
}
