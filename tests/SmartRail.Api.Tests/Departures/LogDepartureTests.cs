using System.Net;
using SmartRail.Api.Tests.Infrastructure;

namespace SmartRail.Api.Tests.Departures;

/// <summary>
/// <c>POST /guard/departure-logs</c>: the Guard's departure log as the offline queue syncs it. EP-06
/// "Done when" is the replay case: a resend of the same client identifier is accepted exactly once
/// (200, nothing changed), and a second log for the same stop is a conflict.
/// </summary>
[Collection(ApiDatabase.Name)]
public sealed class LogDepartureTests(ApiDatabaseFixture database) : ApiTest(database)
{
    [ApiFact]
    public async Task Happy_path_a_departure_is_logged_once_for_the_calling_guard()
    {
        // Arrange
        var journey = await Api.UntouchedJourneyAsync();
        var stop = journey.Stops.First(candidate => !candidate.IsFinalStop);
        var clientId = Guid.CreateVersion7();
        var body = TestData.DepartureBody(journey.Id, stop.Sequence, clientId);

        // Act
        using var response = await Api.PostAsync(Urls.DepartureLogs, Api.Guard, body);

        // Assert
        await response.HasStatusAsync(HttpStatusCode.Created);
        var log = await response.ReadAsync<DepartureLog>();

        Assert.Equal(clientId, log.Id);
        Assert.Equal(journey.Id, log.JourneyId);
        Assert.Equal(stop.Sequence, log.StopSequence);
        Assert.True(
            log.ReceivedAt >= log.DepartedAt,
            $"The receipt time {log.ReceivedAt} is before the device time {log.DepartedAt}.");

        var guardView = await Api.GuardDetailAsync(journey.Id);
        var logged = guardView.Journey.Stops.Single(candidate => candidate.Sequence == stop.Sequence);

        Assert.True(logged.DepartureLogged, "The guard's view does not show the departure that was just logged.");
        Assert.Equal(log.DepartedAt, logged.ActualDeparture);
    }

    [ApiFact]
    public async Task A_resend_of_the_same_log_is_accepted_once_and_changes_nothing()
    {
        // Arrange
        var journey = await Api.UntouchedJourneyAsync();
        var stop = journey.Stops.First(candidate => !candidate.IsFinalStop);
        var body = TestData.DepartureBody(journey.Id, stop.Sequence, Guid.CreateVersion7());

        DepartureLog first;
        using (var accepted = await Api.PostAsync(Urls.DepartureLogs, Api.Guard, body))
        {
            await accepted.HasStatusAsync(HttpStatusCode.Created);
            first = await accepted.ReadAsync<DepartureLog>();
        }

        // Act
        using var response = await Api.PostAsync(Urls.DepartureLogs, Api.Guard, body);

        // Assert
        await response.HasStatusAsync(HttpStatusCode.OK);
        var replayed = await response.ReadAsync<DepartureLog>();

        Assert.Equal(first.Id, replayed.Id);
        Assert.Equal(first.DepartedAt, replayed.DepartedAt);

        var guardView = await Api.GuardDetailAsync(journey.Id);
        Assert.Single(guardView.Journey.Stops, candidate => candidate.DepartureLogged);
    }

    [ApiFact]
    public async Task A_departure_time_without_a_utc_offset_is_a_bad_request()
    {
        // Arrange
        var journey = await Api.UntouchedJourneyAsync();
        var stop = journey.Stops.First(candidate => !candidate.IsFinalStop);

        var body = new
        {
            id = Guid.CreateVersion7(),
            journeyId = journey.Id,
            stopSequence = stop.Sequence,
            departedAt = "2026-10-19T08:15:00",
            delayCause = (string?)null,
        };

        // Act
        using var response = await Api.PostAsync(Urls.DepartureLogs, Api.Guard, body);

        // Assert
        await response.IsProblemAsync(HttpStatusCode.BadRequest);
        Assert.DoesNotContain(
            (await Api.GuardDetailAsync(journey.Id)).Journey.Stops,
            candidate => candidate.DepartureLogged);
    }

    [ApiFact]
    public async Task A_departure_on_an_unknown_journey_is_not_found()
    {
        // Arrange
        var unknownJourneyId = Guid.CreateVersion7();

        // Act
        using var response = await Api.PostAsync(
            Urls.DepartureLogs,
            Api.Guard,
            TestData.DepartureBody(unknownJourneyId, 1));

        // Assert
        await response.IsProblemAsync(HttpStatusCode.NotFound);
    }

    [ApiFact]
    public async Task A_second_log_for_the_same_stop_is_a_conflict_and_keeps_the_first()
    {
        // Arrange
        var journey = await Api.UntouchedJourneyAsync();
        var stop = journey.Stops.First(candidate => !candidate.IsFinalStop);

        DepartureLog first;
        using (var accepted = await Api.PostAsync(
            Urls.DepartureLogs,
            Api.Guard,
            TestData.DepartureBody(journey.Id, stop.Sequence, Guid.CreateVersion7())))
        {
            await accepted.HasStatusAsync(HttpStatusCode.Created);
            first = await accepted.ReadAsync<DepartureLog>();
        }

        // Act
        using var response = await Api.PostAsync(
            Urls.DepartureLogs,
            Api.Guard,
            TestData.DepartureBody(journey.Id, stop.Sequence, Guid.CreateVersion7()));

        // Assert
        await response.IsProblemAsync(HttpStatusCode.Conflict);

        var guardView = await Api.GuardDetailAsync(journey.Id);
        var logged = guardView.Journey.Stops.Single(candidate => candidate.Sequence == stop.Sequence);

        Assert.True(logged.DepartureLogged, "The first departure was lost.");
        Assert.Equal(first.DepartedAt, logged.ActualDeparture);
        Assert.Single(guardView.Journey.Stops, candidate => candidate.DepartureLogged);
    }
}
