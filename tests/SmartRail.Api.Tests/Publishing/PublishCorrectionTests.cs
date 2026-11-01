using System.Net;
using SmartRail.Api.Tests.Infrastructure;

namespace SmartRail.Api.Tests.Publishing;

/// <summary>
/// <c>POST /operations/changes/{changeId}/corrections</c>: withdraws a published change, keeping the
/// original on record (S-12 "corrective re-issue"; EP-08 "Done when" — the change and its correction
/// are both visible).
/// </summary>
[Collection(ApiDatabase.Name)]
public sealed class PublishCorrectionTests(ApiDatabaseFixture database) : ApiTest(database)
{
    [ApiFact]
    public async Task Happy_path_a_published_delay_is_corrected_and_the_original_is_kept()
    {
        // Arrange
        var journey = await Api.UntouchedJourneyAsync();
        var stop = journey.Stops.First(candidate => !candidate.IsFinalStop);
        var original = await Api.PublishDelayAsync(journey.Id, stop.Sequence);

        // Act
        using var response = await Api.PostAsync(Urls.Corrections(original.Id), Api.Controller);

        // Assert
        await response.HasStatusAsync(HttpStatusCode.Created);
        var correction = await response.ReadAsync<JourneyChange>();

        Assert.Equal("correction", correction.Type);
        Assert.Equal(original.Id.ToString(), correction.Values["correctsChangeId"]);
        Assert.Equal("automatically_published", correction.Provenance);
        Assert.False(correction.Corrected);

        // Both rows are on record: the original is marked corrected, not erased.
        var detail = await Api.DetailAsync(journey.Id);
        Assert.Equal(2, detail.Changes.Count);

        Assert.True(
            detail.Changes.Single(change => change.Id == original.Id).Corrected,
            "The corrected change is still on record but not marked corrected.");
        Assert.False(detail.Changes.Single(change => change.Id == correction.Id).Corrected);
    }

    [ApiFact]
    public async Task A_correction_of_an_unknown_change_is_not_found()
    {
        // Arrange
        var unknownChangeId = Guid.CreateVersion7();

        // Act
        using var response = await Api.PostAsync(Urls.Corrections(unknownChangeId), Api.Controller);

        // Assert
        await response.IsProblemAsync(HttpStatusCode.NotFound);
    }

    [ApiFact]
    public async Task Correcting_the_same_change_twice_is_a_conflict_and_leaves_one_correction()
    {
        // Arrange
        var journey = await Api.UntouchedJourneyAsync();
        var stop = journey.Stops.First(candidate => !candidate.IsFinalStop);
        var original = await Api.PublishDelayAsync(journey.Id, stop.Sequence);

        using (var first = await Api.PostAsync(Urls.Corrections(original.Id), Api.Controller))
        {
            await first.HasStatusAsync(HttpStatusCode.Created);
        }

        // Act
        using var response = await Api.PostAsync(Urls.Corrections(original.Id), Api.Controller);

        // Assert
        await response.IsProblemAsync(HttpStatusCode.Conflict);

        var detail = await Api.DetailAsync(journey.Id);
        Assert.Equal(2, detail.Changes.Count);
        Assert.Single(detail.Changes, change => change.Type == "correction");
    }
}
