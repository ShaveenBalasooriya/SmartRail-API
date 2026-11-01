using SmartRail.RailNetwork.Application.Timetable.GetScheduledJourneys;
using SmartRail.RailNetwork.UnitTests.TestDoubles;

namespace SmartRail.RailNetwork.UnitTests.Timetable.GetScheduledJourneys;

/// <summary>
/// The scheduled journeys query's input rule (journey-generation spec): only today or tomorrow in the
/// railway's time zone is a day the record holds, and a missing date means today, so it is accepted.
/// The error is keyed <c>date</c>.
/// </summary>
public sealed class GetScheduledJourneysQueryValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(1)]
    public void Happy_path_accepts_today_tomorrow_or_no_date(int? daysFromToday)
    {
        // Arrange
        var validator = new GetScheduledJourneysQueryValidator(TestData.Clock());
        DateOnly? date = daysFromToday is { } offset ? TestData.ServiceDate.AddDays(offset) : null;
        var query = new GetScheduledJourneysQuery(date);

        // Act
        var result = validator.Validate(query);

        // Assert
        Assert.True(result.IsValid, $"{date} should be an acceptable day.");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(365)]
    public void Any_other_day_is_rejected_on_the_date_field(int daysFromToday)
    {
        // Arrange
        var validator = new GetScheduledJourneysQueryValidator(TestData.Clock());
        var date = TestData.ServiceDate.AddDays(daysFromToday);
        var query = new GetScheduledJourneysQuery(date);

        // Act
        var result = validator.Validate(query);

        // Assert
        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Equal("date", error.PropertyName);
        Assert.Equal("The date must be today or tomorrow in the railway's time zone.", error.ErrorMessage);
    }

    [Fact]
    public void A_null_clock_is_rejected()
    {
        // Arrange

        // Act
        var exception = Assert.Throws<ArgumentNullException>(
            () => new GetScheduledJourneysQueryValidator(clock: null!));

        // Assert
        Assert.Equal("clock", exception.ParamName);
    }
}
