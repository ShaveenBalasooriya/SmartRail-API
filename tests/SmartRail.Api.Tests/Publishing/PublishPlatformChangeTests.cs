using System.Net;
using SmartRail.Api.Tests.Infrastructure;

namespace SmartRail.Api.Tests.Publishing;

/// <summary>
/// <c>POST /operations/journeys/{journeyId}/platform-changes</c>: moves a calling stop that has not
/// departed to another platform of its station (EP-08; M-09 "platform change on every surface").
/// </summary>
[Collection(ApiDatabase.Name)]
public sealed class PublishPlatformChangeTests(ApiDatabaseFixture database) : ApiTest(database)
{
    [ApiFact]
    public async Task Happy_path_a_calling_stop_moves_to_another_platform_of_its_station()
    {
        // Arrange
        var move = await Api.PlatformMoveAsync();

        // Act
        using var response = await Api.PostAsync(
            Urls.PlatformChanges(move.Journey.Id),
            Api.Controller,
            new { stopSequence = move.Stop.Sequence, platformId = move.PlatformId });

        // Assert
        await response.HasStatusAsync(HttpStatusCode.Created);
        var published = await response.ReadAsync<JourneyChange>();

        Assert.Equal("platform_change", published.Type);
        Assert.Equal(move.PlatformId.ToString(), published.Values["platformId"]);
        Assert.Equal(move.Stop.Platform!.Id.ToString(), published.Values["previousPlatformId"]);
        Assert.Equal("automatically_published", published.Provenance);

        var detail = await Api.DetailAsync(move.Journey.Id);
        var stop = detail.Stops.Single(candidate => candidate.Sequence == move.Stop.Sequence);

        Assert.Equal(move.PlatformId, stop.Platform?.Id);
        Assert.Equal(published.Id, Assert.Single(detail.Changes).Id);
    }

    [ApiFact]
    public async Task A_platform_change_to_a_platform_of_another_station_is_a_bad_request()
    {
        // Arrange
        var move = await Api.PlatformMoveAsync();

        // Act
        using var response = await Api.PostAsync(
            Urls.PlatformChanges(move.Journey.Id),
            Api.Controller,
            new { stopSequence = move.Stop.Sequence, platformId = move.ForeignPlatformId });

        // Assert
        await response.IsProblemAsync(HttpStatusCode.BadRequest);
        Assert.Empty((await Api.DetailAsync(move.Journey.Id)).Changes);
    }

    [ApiFact]
    public async Task A_platform_change_on_an_unknown_journey_is_not_found()
    {
        // Arrange
        var unknownJourneyId = Guid.CreateVersion7();

        // Act
        using var response = await Api.PostAsync(
            Urls.PlatformChanges(unknownJourneyId),
            Api.Controller,
            new { stopSequence = 1, platformId = Guid.CreateVersion7() });

        // Assert
        await response.IsProblemAsync(HttpStatusCode.NotFound);
    }

    [ApiFact]
    public async Task A_platform_change_to_the_platform_the_stop_already_uses_is_a_conflict()
    {
        // Arrange
        var move = await Api.PlatformMoveAsync();

        // Act
        using var response = await Api.PostAsync(
            Urls.PlatformChanges(move.Journey.Id),
            Api.Controller,
            new { stopSequence = move.Stop.Sequence, platformId = move.Stop.Platform!.Id });

        // Assert
        await response.IsProblemAsync(HttpStatusCode.Conflict);
        Assert.Empty((await Api.DetailAsync(move.Journey.Id)).Changes);
    }
}
