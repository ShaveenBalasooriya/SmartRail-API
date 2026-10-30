using System.Globalization;
using SmartRail.Catering.Application.Provider.Journeys.GetStationJourneys;

namespace SmartRail.Catering.UnitTests.Provider;

/// <summary>
/// The station journeys date rule (provider-data-api spec, "Journeys calling at a station"): only
/// today and tomorrow in the railway's time zone are accepted, an absent date means today, and the
/// error is keyed by the query parameter. This is the one part of the read that is a rule rather than
/// a pass-through to another module's contract.
/// </summary>
public sealed class GetStationJourneysQueryValidatorTests
{
    /// <summary>Noon in the railway's time zone on the day the validator is checked against.</summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.FromHours(5.5));

    private static readonly TimeZoneInfo RailwayTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Colombo");

    private static readonly DateOnly Today = new(2026, 10, 29);

    [Theory]
    [InlineData(null)]
    [InlineData("2026-10-29")]
    [InlineData("2026-10-30")]
    public void Today_tomorrow_and_an_absent_date_are_accepted(string? date)
    {
        var result = Validate(date is null ? null : DateOnly.Parse(date, CultureInfo.InvariantCulture));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("2026-10-28")]
    [InlineData("2026-10-31")]
    public void A_past_date_or_one_beyond_tomorrow_is_refused_and_keyed_by_date(string date)
    {
        var result = Validate(DateOnly.Parse(date, CultureInfo.InvariantCulture));

        Assert.False(result.IsValid);

        var failure = Assert.Single(result.Errors);
        Assert.Equal("date", failure.PropertyName);
        Assert.Contains("today or tomorrow", failure.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_station_identifier_is_refused_and_keyed_by_the_route_parameter()
    {
        var result = new GetStationJourneysQueryValidator(new FixedTimeProvider(Now), RailwayTimeZone)
            .Validate(new GetStationJourneysQuery(Guid.Empty, Today));

        Assert.False(result.IsValid);

        var failure = Assert.Single(result.Errors);
        Assert.Equal("id", failure.PropertyName);
    }

    private static FluentValidation.Results.ValidationResult Validate(DateOnly? date) =>
        new GetStationJourneysQueryValidator(new FixedTimeProvider(Now), RailwayTimeZone)
            .Validate(new GetStationJourneysQuery(Guid.CreateVersion7(), date));

    /// <summary>A clock frozen at noon on the day the rule is checked against.</summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
