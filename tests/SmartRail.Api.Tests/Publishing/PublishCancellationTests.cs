using System.Net;
using SmartRail.Api.Tests.Infrastructure;

namespace SmartRail.Api.Tests.Publishing;

/// <summary>
/// <c>POST /operations/journeys/{journeyId}/cancellation</c>: cancels a journey that has no recorded
/// departure (EP-08; M-16 "structured status publication").
/// </summary>
[Collection(ApiDatabase.Name)]
public sealed class PublishCancellationTests(ApiDatabaseFixture database) : ApiTest(database)
{
    [ApiFact]
    public async Task Happy_path_a_journey_with_no_departure_is_cancelled_and_reported_cancelled()
    {
        // Arrange
        var journey = await Api.UntouchedJourneyAsync();

        // Act
        using var response = await Api.PostAsync(Urls.Cancellation(journey.Id), Api.Controller);

        // Assert
        await response.HasStatusAsync(HttpStatusCode.Created);
        var published = await response.ReadAsync<JourneyChange>();

        Assert.Equal("cancellation", published.Type);
        Assert.Empty(published.Values);
        Assert.Equal("automatically_published", published.Provenance);
        Assert.False(published.Corrected);

        var detail = await Api.DetailAsync(journey.Id);
        Assert.Equal("cancelled", detail.Journey.Status);
        Assert.Equal(published.Id, Assert.Single(detail.Changes).Id);
    }

    [ApiFact]
    public async Task A_cancellation_of_an_unknown_journey_is_not_found()
    {
        // Arrange
        var unknownJourneyId = Guid.CreateVersion7();

        // Act
        using var response = await Api.PostAsync(Urls.Cancellation(unknownJourneyId), Api.Controller);

        // Assert
        await response.IsProblemAsync(HttpStatusCode.NotFound);
    }

    [ApiFact]
    public async Task A_cancellation_after_a_departure_is_a_conflict_and_changes_nothing()
    {
        // Arrange
        var journey = await Api.UntouchedJourneyAsync();
        var stop = journey.Stops.First(candidate => !candidate.IsFinalStop);

        using (var departure = await Api.PostAsync(
            Urls.DepartureLogs,
            Api.Guard,
            TestData.DepartureBody(journey.Id, stop.Sequence)))
        {
            await departure.HasStatusAsync(HttpStatusCode.Created);
        }

        // Act
        using var response = await Api.PostAsync(Urls.Cancellation(journey.Id), Api.Controller);

        // Assert
        await response.IsProblemAsync(HttpStatusCode.Conflict);

        var detail = await Api.DetailAsync(journey.Id);
        Assert.NotEqual("cancelled", detail.Journey.Status);
        Assert.Empty(detail.Changes);
    }
}
