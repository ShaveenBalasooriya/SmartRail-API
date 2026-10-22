using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using SmartRail.SharedKernel.Api;
using SmartRail.SharedKernel.Application.Behaviors;
using SmartRail.SharedKernel.Application.Messaging;
using SmartRail.SharedKernel.Results;

namespace SmartRail.SharedKernel.UnitTests.Results;

public sealed class ValidationPipelineBehaviorTests
{
    [Fact]
    public async Task A_failing_request_skips_the_handler_and_carries_every_error()
    {
        var validator = new InlineValidator<ProbeQuery>();
        validator.RuleFor(query => query.Direction).NotEmpty().WithMessage("The direction is required.");
        validator.RuleFor(query => query.Date).NotEmpty().WithMessage("The date is required.");

        var behavior = new ValidationPipelineBehavior<ProbeQuery, Result<int>>([validator]);
        var handlerRan = false;

        var response = await behavior.Handle(
            new ProbeQuery(null, null),
            _ =>
            {
                handlerRan = true;
                return Task.FromResult(Result.Success(1));
            },
            CancellationToken.None);

        Assert.False(handlerRan);

        var failure = Assert.IsType<ValidationResult<int>>(response);
        Assert.Equal(2, failure.Errors.Count);
        Assert.Contains(failure.Errors, error => error.Code == nameof(ProbeQuery.Direction));
        Assert.Contains(failure.Errors, error => error.Code == nameof(ProbeQuery.Date));
    }

    [Fact]
    public async Task A_valid_request_reaches_its_handler()
    {
        var validator = new InlineValidator<ProbeQuery>();
        validator.RuleFor(query => query.Direction).NotEmpty();

        var behavior = new ValidationPipelineBehavior<ProbeQuery, Result<int>>([validator]);

        var response = await behavior.Handle(
            new ProbeQuery("outbound", "2026-10-27"),
            _ => Task.FromResult(Result.Success(7)),
            CancellationToken.None);

        Assert.True(response.IsSuccess);
        Assert.Equal(7, response.Value);
    }

    private sealed record ProbeQuery(string? Direction, string? Date) : IQuery<int>;
}

public sealed class ResultProblemDetailsTests
{
    [Theory]
    [InlineData(ErrorType.Validation, StatusCodes.Status400BadRequest)]
    [InlineData(ErrorType.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(ErrorType.Conflict, StatusCodes.Status409Conflict)]
    [InlineData(ErrorType.Unauthorized, StatusCodes.Status401Unauthorized)]
    [InlineData(ErrorType.Forbidden, StatusCodes.Status403Forbidden)]
    [InlineData(ErrorType.Failure, StatusCodes.Status500InternalServerError)]
    public void Each_error_type_maps_to_its_http_status(ErrorType errorType, int expectedStatus)
    {
        var result = Result.Failure(new Error("probe.failed", "The probe failed.", errorType));

        var problem = Assert.IsType<ProblemHttpResult>(result.ToProblemDetails());

        Assert.Equal(expectedStatus, problem.StatusCode);
    }

    [Fact]
    public void A_validation_failure_lists_every_error_by_field()
    {
        var result = new ValidationResult(
        [
            Error.Validation(nameof(ProbeQuery.Direction), "The direction must be outbound or inbound."),
            Error.Validation(nameof(ProbeQuery.Date), "The date must be today or tomorrow."),
        ]);

        var problem = Assert.IsType<ProblemHttpResult>(result.ToProblemDetails());

        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Null(problem.ProblemDetails.Detail);

        var errors = Assert.IsType<Dictionary<string, string[]>>(problem.ProblemDetails.Extensions["errors"]);
        Assert.Equal(["The direction must be outbound or inbound."], errors[nameof(ProbeQuery.Direction)]);
        Assert.Equal(["The date must be today or tomorrow."], errors[nameof(ProbeQuery.Date)]);
    }

    private sealed record ProbeQuery(string Direction, string Date) : IQuery<int>;
}
