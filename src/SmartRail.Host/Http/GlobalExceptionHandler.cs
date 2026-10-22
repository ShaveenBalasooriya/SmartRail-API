using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace SmartRail.Host.Http;

/// <summary>
/// Turns every unhandled exception into an RFC 9457 problem-details response. Outside Development
/// the response carries a generic title and a <c>traceId</c> and nothing about the exception; in
/// Development the detail is added to help debugging (api-conventions spec, EP-15).
/// </summary>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    IHostEnvironment environment,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        var (statusCode, title) = Classify(exception);
        var routeTemplate = ProblemDetailsConfiguration.RouteTemplateOf(httpContext);
        var correlationId = ProblemDetailsConfiguration.CorrelationIdOf(httpContext);

        // The exception detail goes to the diagnostic log only. Logging it is how an operator
        // diagnoses the fault; the client never sees it outside Development.
        logger.LogError(
            exception,
            "Unhandled request failure. Status {StatusCode} for {Method} {RouteTemplate} (correlation {CorrelationId})",
            statusCode,
            httpContext.Request.Method,
            routeTemplate,
            correlationId);

        httpContext.Response.StatusCode = statusCode;

        var problem = BuildProblem(statusCode, title, exception);

        if (environment.IsDevelopment() && problem.Detail is null)
        {
            problem.Detail = exception.Message;
        }

        problem.Extensions["traceId"] = correlationId;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    /// <summary>
    /// Maps an exception to the status the client should see. A request that could not be read is
    /// the caller's fault and is reported as a validation failure naming the field at fault;
    /// everything else is a server fault.
    /// </summary>
    public static (int StatusCode, string Title) Classify(Exception exception) => exception switch
    {
        BadHttpRequestException => (StatusCodes.Status400BadRequest, "The request could not be read."),
        _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred."),
    };

    /// <summary>
    /// Builds the problem details for a failure. A JSON body that does not satisfy the contract
    /// carries the offending field so a client can fix exactly that field.
    /// </summary>
    public static ProblemDetails BuildProblem(int statusCode, string title, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is BadHttpRequestException { InnerException: JsonException jsonException })
        {
            var field = FieldFromPath(jsonException.Path);
            return new HttpValidationProblemDetails(new Dictionary<string, string[]>
            {
                [field] = [jsonException.Message],
            })
            {
                Status = statusCode,
                Title = title,
            };
        }

        return new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Type = $"https://smartrail.local/problems/{statusCode}",
        };
    }

    /// <summary>
    /// Turns a JSON path such as <c>$.departureAt</c> into the field name a client sent, so the
    /// validation error can be matched to a request property.
    /// </summary>
    public static string FieldFromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "request";
        }

        return path.StartsWith("$.", StringComparison.Ordinal) ? path[2..] : path.TrimStart('$').TrimStart('.');
    }
}
