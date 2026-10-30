using FluentValidation;

namespace SmartRail.Catering.Application.Provider.Journeys.GetStationJourneys;

/// <summary>
/// The station identifier is required, and the date must be today or tomorrow in the railway's time
/// zone; a missing date defaults to today in the handler (provider-data-api spec, "Journeys calling
/// at a station"). The errors are keyed <c>id</c> and <c>date</c>.
/// </summary>
public sealed class GetStationJourneysQueryValidator : AbstractValidator<GetStationJourneysQuery>
{
    public GetStationJourneysQueryValidator(TimeProvider timeProvider, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(timeZone);

        RuleFor(query => query.StationId)
            .NotEmpty()
            .WithMessage("The station identifier is required.")
            .OverridePropertyName("id");

        RuleFor(query => query.Date)
            .Must(date => date is null || date == Today(timeProvider, timeZone) || date == Today(timeProvider, timeZone).AddDays(1))
            .WithMessage("The date must be today or tomorrow in the railway's time zone.")
            .OverridePropertyName("date");
    }

    /// <summary>Today on the railway's calendar, the day an absent date means.</summary>
    private static DateOnly Today(TimeProvider timeProvider, TimeZoneInfo timeZone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), timeZone).DateTime);
}
