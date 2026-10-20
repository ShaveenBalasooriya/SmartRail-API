using Microsoft.AspNetCore.Http;
using SmartRail.SharedKernel.Results;

namespace SmartRail.SharedKernel.Api;

/// <summary>
/// Turns a failed <see cref="Result"/> into the RFC 9457 problem details every error response uses
/// (api-conventions spec, design D1). The Host's problem-details customisation adds <c>traceId</c>
/// and <c>type</c> afterwards, so they are not set here.
/// </summary>
public static class ResultExtensions
{
    /// <summary>Maps a failed result to problem details.</summary>
    /// <exception cref="InvalidOperationException">The result succeeded; it has no error to map.</exception>
    public static IResult ToProblemDetails(this Result result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsSuccess)
        {
            throw new InvalidOperationException(
                "A successful result has no problem details; return the success response instead.");
        }

        IReadOnlyList<Error>? errors = result switch
        {
            IValidationResult validation => validation.Errors,
            { Error.Type: ErrorType.Validation } => [result.Error],
            _ => null,
        };

        if (errors is not null)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: TitleFor(StatusCodes.Status400BadRequest),
                extensions: new Dictionary<string, object?>
                {
                    ["errors"] = FieldErrors(errors),
                });
        }

        var statusCode = StatusFor(result.Error.Type);

        return TypedResults.Problem(
            statusCode: statusCode,
            title: TitleFor(statusCode),

            // A server fault never explains itself outside Development (api-conventions spec).
            detail: result.Error.Type == ErrorType.Failure ? null : result.Error.Description);
    }

    /// <summary>The HTTP status an error type maps to.</summary>
    public static int StatusFor(ErrorType errorType) => errorType switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        _ => StatusCodes.Status500InternalServerError,
    };

    /// <summary>The standard title for a status code.</summary>
    public static string TitleFor(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "The request is invalid.",
        StatusCodes.Status401Unauthorized => "Authentication is required.",
        StatusCodes.Status403Forbidden => "You may not perform this action.",
        StatusCodes.Status404NotFound => "No resource matches this request.",
        StatusCodes.Status409Conflict => "The request conflicts with the current state.",
        _ => "The request could not be completed.",
    };

    private static Dictionary<string, string[]> FieldErrors(IReadOnlyList<Error> errors)
    {
        var fieldErrors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        foreach (var error in errors)
        {
            var field = string.IsNullOrWhiteSpace(error.Code) ? "request" : error.Code;

            fieldErrors[field] = fieldErrors.TryGetValue(field, out var messages)
                ? [.. messages, error.Description]
                : [error.Description];
        }

        return fieldErrors;
    }
}
