using System.Globalization;
using System.Net;
using SmartRail.Api.Tests.Infrastructure;

namespace SmartRail.Api.Tests.Publishing;

/// <summary>
/// <c>POST /operations/journeys/{journeyId}/delays</c>: the Network Controller publishes a delay from
/// a calling stop that has not departed (EP-08 "Done when" — the published change is visible on the
/// channel the API serves; EP-07 for the expected times it moves).
/// </summary>
[Collection(ApiDatabase.Name)]
public sealed class PublishDelayTests(ApiDatabaseFixture database) : ApiTest(database)
{
    [ApiFact]
    public async Task Happy_path_a_delay_is_published_and_appears_on_the_journey()
    {
        // Arrange
        var journey = await Api.UntouchedJourneyAsync();
        var stop = journey.Stops.First(candidate => !candidate.IsFinalStop);

        // Act
        using var response = await Api.PostAsync(
            Urls.Delays(journey.Id),
            Api.Controller,
            new { fromStopSequence = stop.Sequence, minutes = 15 });

        // Assert
        await response.HasStatusAsync(HttpStatusCode.Created);
        var published = await response.ReadAsync<JourneyChange>();

        Assert.Equal("delay", published.Type);
        Assert.Equal(stop.Sequence.ToString(CultureInfo.InvariantCulture), published.Values["fromStopSequence"]);
        Assert.Equal("15", published.Values["minutes"]);
        Assert.Equal("automatically_published", published.Provenance);
        Assert.False(published.Corrected);
        Assert.False(string.IsNullOrWhiteSpace(published.PublisherName));
        Assert.True(
            published.PublishedAt > DateTimeOffset.UtcNow.AddMinutes(-1),
            $"PublishedAt {published.PublishedAt} is not the time the change was published.");

        // The change is on the journey's record, in the same shape the publish answered with.
        var detail = await Api.DetailAsync(journey.Id);
        var shown = Assert.Single(detail.Changes);

        Assert.Equal(published.Id, shown.Id);
        Assert.Equal("delay", shown.Type);
        Assert.Equal("15", shown.Values["minutes"]);
    }

    [ApiTheory]
    [InlineData(0)]
    [InlineData(721)]
    public async Task A_delay_outside_one_to_720_minutes_is_a_bad_request(int minutes)
    {
        // Arrange
        var journey = await Api.UntouchedJourneyAsync();
        var stop = journey.Stops.First(candidate => !candidate.IsFinalStop);

        // Act
        using var response = await Api.PostAsync(
            Urls.Delays(journey.Id),
            Api.Controller,
            new { fromStopSequence = stop.Sequence, minutes });

        // Assert
        await response.IsProblemAsync(HttpStatusCode.BadRequest);
        Assert.Empty((await Api.DetailAsync(journey.Id)).Changes);
    }

    [ApiFact]
    public async Task A_delay_on_an_unknown_journey_is_not_found()
    {
        // Arrange
        var unknownJourneyId = Guid.CreateVersion7();

        // Act
        using var response = await Api.PostAsync(
            Urls.Delays(unknownJourneyId),
            Api.Controller,
            new { fromStopSequence = 1, minutes = 15 });

        // Assert
        await response.IsProblemAsync(HttpStatusCode.NotFound);
    }

    [ApiFact]
    public async Task A_delay_from_a_stop_that_already_departed_is_a_conflict()
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
        using var response = await Api.PostAsync(
            Urls.Delays(journey.Id),
            Api.Controller,
            new { fromStopSequence = stop.Sequence, minutes = 15 });

        // Assert
        await response.IsProblemAsync(HttpStatusCode.Conflict);
        Assert.Empty((await Api.DetailAsync(journey.Id)).Changes);
    }
}
