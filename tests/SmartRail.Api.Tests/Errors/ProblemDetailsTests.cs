using System.Net;
using SmartRail.Api.Tests.Infrastructure;

namespace SmartRail.Api.Tests.Errors;

/// <summary>
/// The error format every 4xx answer uses (api-conventions spec; test-suite-review §5a phase C,
/// item 4): <c>application/problem+json</c>, the request's correlation ID in the body and in the
/// <c>X-Correlation-Id</c> header, and no stack trace or exception type name.
/// </summary>
/// <remarks>
/// <see cref="ApiAssertions.IsProblemAsync"/> makes these four checks on every 4xx any test triggers,
/// so this class adds the explicit case per status family a client has to handle rather than the
/// only coverage. One case per family, each with the cheapest route that produces it.
/// </remarks>
[Collection(ApiDatabase.Name)]
public sealed class ProblemDetailsTests(ApiDatabaseFixture database) : ApiTest(database)
{
    [ApiFact]
    public async Task A_validation_failure_is_problem_details_with_the_correlation_id()
    {
        // Arrange
        var journey = await Api.UntouchedJourneyAsync();
        var stop = journey.Stops.First(candidate => !candidate.IsFinalStop);

        // Act
        using var response = await Api.PostAsync(
            Urls.Delays(journey.Id),
            Api.Controller,
            new { fromStopSequence = stop.Sequence, minutes = 0 });

        // Assert
        await response.IsProblemAsync(HttpStatusCode.BadRequest);
    }

    [ApiFact]
    public async Task An_unauthenticated_request_is_problem_details_with_the_correlation_id()
    {
        // Act
        using var response = await Api.PostAsync(
            Urls.Delays(Guid.CreateVersion7()),
            Credential.None,
            new { fromStopSequence = 1, minutes = 5 });

        // Assert
        await response.IsProblemAsync(HttpStatusCode.Unauthorized);
    }

    [ApiFact]
    public async Task A_forbidden_request_is_problem_details_with_the_correlation_id()
    {
        // Act
        using var response = await Api.PostAsync(
            Urls.Delays(Guid.CreateVersion7()),
            Api.Guard,
            new { fromStopSequence = 1, minutes = 5 });

        // Assert
        await response.IsProblemAsync(HttpStatusCode.Forbidden);
    }

    [ApiFact]
    public async Task An_unknown_resource_is_problem_details_with_the_correlation_id()
    {
        // Act
        using var response = await Api.PostAsync(
            Urls.Delays(Guid.CreateVersion7()),
            Api.Controller,
            new { fromStopSequence = 1, minutes = 5 });

        // Assert
        await response.IsProblemAsync(HttpStatusCode.NotFound);
    }

    [ApiFact]
    public async Task A_conflict_is_problem_details_with_the_correlation_id()
    {
        // Arrange
        var journey = await Api.UntouchedJourneyAsync();

        using (var cancelled = await Api.PostAsync(Urls.Cancellation(journey.Id), Api.Controller))
        {
            await cancelled.HasStatusAsync(HttpStatusCode.Created);
        }

        // Act
        using var response = await Api.PostAsync(Urls.Cancellation(journey.Id), Api.Controller);

        // Assert
        await response.IsProblemAsync(HttpStatusCode.Conflict);
    }
}
