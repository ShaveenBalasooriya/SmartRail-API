using System.Globalization;
using System.Net;
using SmartRail.Api.Tests.Infrastructure;

namespace SmartRail.Api.Tests.Publishing;

/// <summary>
/// <c>POST /operations/journeys/{journeyId}/termination</c>: ends a running journey before its
/// terminus at the last stop the train served (EP-08; M-16's early termination).
/// </summary>
[Collection(ApiDatabase.Name)]
public sealed class PublishTerminationTests(ApiDatabaseFixture database) : ApiTest(database)
{
    [ApiFact]
    public async Task Happy_path_a_running_journey_is_ended_at_the_last_stop_it_served()
    {
        // Arrange
        var journey = await Api.UntouchedJourneyAsync();
        var stop = journey.Stops.First(candidate => !candidate.IsFinalStop);

        // A journey that has not departed is cancelled instead, so the guard departs one stop first.
        using (var departure = await Api.PostAsync(
            Urls.DepartureLogs,
            Api.Guard,
            TestData.DepartureBody(journey.Id, stop.Sequence)))
        {
            await departure.HasStatusAsync(HttpStatusCode.Created);
        }

        // Act
        using var response = await Api.PostAsync(
            Urls.Termination(journey.Id),
            Api.Controller,
            new { lastStopSequence = stop.Sequence });

        // Assert
        await response.HasStatusAsync(HttpStatusCode.Created);
        var published = await response.ReadAsync<JourneyChange>();

        Assert.Equal("termination", published.Type);
        Assert.Equal(stop.Sequence.ToString(CultureInfo.InvariantCulture), published.Values["lastStopSequence"]);
        Assert.Equal("automatically_published", published.Provenance);

        var detail = await Api.DetailAsync(journey.Id);
        Assert.Equal("terminated", detail.Journey.Status);
        Assert.Equal(published.Id, Assert.Single(detail.Changes).Id);
    }

    [ApiFact]
    public async Task An_early_termination_of_a_journey_that_has_not_started_is_a_conflict()
    {
        // Arrange
        var journey = await Api.UntouchedJourneyAsync();
        var stop = journey.Stops.First(candidate => !candidate.IsFinalStop);

        // Act
        using var response = await Api.PostAsync(
            Urls.Termination(journey.Id),
            Api.Controller,
            new { lastStopSequence = stop.Sequence });

        // Assert
        await response.IsProblemAsync(HttpStatusCode.Conflict);
        Assert.Empty((await Api.DetailAsync(journey.Id)).Changes);
    }

    [ApiFact]
    public async Task An_early_termination_at_the_final_stop_is_a_bad_request()
    {
        // Arrange
        var journey = await Api.UntouchedJourneyAsync();
        var firstStop = journey.Stops.First(candidate => !candidate.IsFinalStop);
        var finalStop = journey.Stops.First(candidate => candidate.IsFinalStop);

        using (var departure = await Api.PostAsync(
            Urls.DepartureLogs,
            Api.Guard,
            TestData.DepartureBody(journey.Id, firstStop.Sequence)))
        {
            await departure.HasStatusAsync(HttpStatusCode.Created);
        }

        // Act
        using var response = await Api.PostAsync(
            Urls.Termination(journey.Id),
            Api.Controller,
            new { lastStopSequence = finalStop.Sequence });

        // Assert
        await response.IsProblemAsync(HttpStatusCode.BadRequest);
        Assert.Empty((await Api.DetailAsync(journey.Id)).Changes);
    }

    [ApiFact]
    public async Task An_early_termination_of_an_unknown_journey_is_not_found()
    {
        // Arrange
        var unknownJourneyId = Guid.CreateVersion7();

        // Act
        using var response = await Api.PostAsync(
            Urls.Termination(unknownJourneyId),
            Api.Controller,
            new { lastStopSequence = 1 });

        // Assert
        await response.IsProblemAsync(HttpStatusCode.NotFound);
    }
}
