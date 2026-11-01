using SmartRail.RailNetwork.Application.Timetable;
using SmartRail.RailNetwork.Application.Timetable.GetScheduledJourneys;
using SmartRail.RailNetwork.Application.Timetable.GetService;
using SmartRail.RailNetwork.Contracts;
using SmartRail.RailNetwork.UnitTests.TestDoubles;
using SmartRail.SharedKernel.Results;

namespace SmartRail.RailNetwork.UnitTests.Timetable;

/// <summary>
/// The timetable contract's implementation (design D3, architecture section 3). Each call is exactly
/// one of the module's own queries, and because the contract cannot report an error, an unknown
/// service becomes <c>null</c> and a rejected date becomes no journeys.
/// </summary>
public sealed class TimetableServiceTests
{
    [Fact]
    public async Task Happy_path_get_service_sends_the_identifier_and_returns_the_service()
    {
        // Arrange
        var service = TestData.ScheduledService();
        var sender = new StubSender { ServiceResult = Result.Success(service) };
        var timetable = new TimetableService(sender);

        // Act
        var result = await timetable.GetServiceAsync(service.Id);

        // Assert
        Assert.Same(service, result);
        var query = Assert.IsType<GetServiceQuery>(Assert.Single(sender.Sent));
        Assert.Equal(service.Id, query.ServiceId);
    }

    [Fact]
    public async Task An_unknown_service_is_null()
    {
        // Arrange
        var sender = new StubSender
        {
            ServiceResult = Result.Failure<ServiceDto>(Error.NotFound("service.not_found", "No service.")),
        };
        var timetable = new TimetableService(sender);

        // Act
        var result = await timetable.GetServiceAsync(Guid.CreateVersion7());

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task Happy_path_get_scheduled_journeys_sends_the_day_and_returns_the_journeys()
    {
        // Arrange
        var journey = TestData.ScheduledJourney(serviceDate: TestData.ServiceDate);
        var sender = new StubSender { JourneysResult = Result.Success<IReadOnlyList<JourneyDto>>([journey]) };
        var timetable = new TimetableService(sender);

        // Act
        var result = await timetable.GetScheduledJourneysAsync(TestData.ServiceDate);

        // Assert
        Assert.Same(journey, Assert.Single(result));
        var query = Assert.IsType<GetScheduledJourneysQuery>(Assert.Single(sender.Sent));
        Assert.Equal(TestData.ServiceDate, query.Date);
    }

    [Fact]
    public async Task A_rejected_day_returns_no_journeys()
    {
        // Arrange
        var sender = new StubSender
        {
            JourneysResult = Result.Failure<IReadOnlyList<JourneyDto>>(
                Error.Validation("date.invalid", "The date must be today or tomorrow.")),
        };
        var timetable = new TimetableService(sender);

        // Act
        var result = await timetable.GetScheduledJourneysAsync(TestData.ServiceDate.AddDays(10));

        // Assert
        Assert.Empty(result);
    }
}
