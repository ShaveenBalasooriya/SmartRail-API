using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;

namespace SmartRail.Host.Http;

/// <summary>
/// Applies the shared parts of every problem-details response: the <c>traceId</c> (which is the
/// request's correlation ID) and, for a validation failure, an <c>errors</c> object. Stack traces
/// and exception text are never added here.
/// </summary>
public static class ProblemDetailsConfiguration
{
    /// <summary>
    /// Configures the problem-details service once, for the whole application.
    /// </summary>
    public static IServiceCollection AddSmartRailProblemDetails(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            var httpContext = context.HttpContext;

            context.ProblemDetails.Status ??= httpContext.Response.StatusCode;
            context.ProblemDetails.Extensions["traceId"] = httpContext.TraceIdentifier;

            if (string.IsNullOrEmpty(context.ProblemDetails.Type))
            {
                context.ProblemDetails.Type = $"https://smartrail.local/problems/{context.ProblemDetails.Status}";
            }

            // A validation failure lists its fields; a server fault never explains itself.
            if (context.ProblemDetails is HttpValidationProblemDetails validation
                && validation.Errors.Count > 0)
            {
                context.ProblemDetails.Extensions["errors"] = validation.Errors;
            }
        });

        return services;
    }

    /// <summary>
    /// The pipeline piece that gives a bodiless error status (404 from routing, 415 from the
    /// content-type check) the same problem-details shape as every other error.
    /// </summary>
    public static IApplicationBuilder UseSmartRailStatusCodePages(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseStatusCodePages(async context =>
        {
            var httpContext = context.HttpContext;
            var problemDetailsService = httpContext.RequestServices.GetRequiredService<IProblemDetailsService>();

            await problemDetailsService.WriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = new Microsoft.AspNetCore.Mvc.ProblemDetails
                {
                    Status = httpContext.Response.StatusCode,
                    Title = TitleFor(httpContext.Response.StatusCode),
                    Type = $"https://smartrail.local/problems/{httpContext.Response.StatusCode}",
                },
            });
        });
    }

    /// <summary>The standard title for a status code that has no more specific description.</summary>
    public static string TitleFor(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "The request is invalid.",
        StatusCodes.Status401Unauthorized => "Authentication is required.",
        StatusCodes.Status403Forbidden => "You may not perform this action.",
        StatusCodes.Status404NotFound => "No resource matches this request.",
        StatusCodes.Status405MethodNotAllowed => "This method is not supported for this resource.",
        StatusCodes.Status415UnsupportedMediaType => "The request body must be application/json.",
        StatusCodes.Status429TooManyRequests => "Too many requests.",
        _ => "The request could not be completed.",
    };

    /// <summary>
    /// The route template for a request, used by logging and error reporting so no identifier from
    /// a raw path is recorded.
    /// </summary>
    public static string RouteTemplateOf(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        // The exception handler clears the endpoint before it hands the request to an
        // IExceptionHandler, so the endpoint that was matched is read back from the exception
        // feature when it is present.
        var endpoint = httpContext.Features.Get<IEndpointFeature>()?.Endpoint
            ?? httpContext.Features.Get<IExceptionHandlerFeature>()?.Endpoint;

        if (endpoint is RouteEndpoint { RoutePattern.RawText: { Length: > 0 } template })
        {
            return template;
        }

        return "unmatched";
    }

    /// <summary>The correlation ID of the request, as set by the correlation middleware.</summary>
    public static string CorrelationIdOf(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        return httpContext.Items.TryGetValue(CorrelationIdMiddleware.LogScopeKey, out var value) && value is string id
            ? id
            : httpContext.TraceIdentifier;
    }
}
