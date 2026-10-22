using System.Diagnostics;

namespace SmartRail.Host.Http;

/// <summary>
/// Writes exactly one structured log entry per completed request with the method, the route
/// template, the status, the duration and the correlation ID (diagnostic-logging spec, EP-17).
/// </summary>
/// <remarks>
/// This is deliberately not <c>UseHttpLogging</c>: that middleware can record headers and bodies,
/// and this design keeps tokens, API keys and cookies out by construction rather than by
/// configuration. Nothing here reads a header other than the correlation ID that the correlation
/// middleware has already validated.
/// </remarks>
public sealed class RequestLoggingMiddleware(RequestDelegate next)
{
    public const string MessageTemplate =
        "HTTP {Method} {RouteTemplate} responded {StatusCode} in {DurationMs} ms (correlation {CorrelationId})";

    public async Task InvokeAsync(HttpContext context, ILogger<RequestLoggingMiddleware> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(logger);

        var started = Stopwatch.GetTimestamp();
        try
        {
            await next(context);
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(started);
            var statusCode = context.Response.StatusCode;

            // The route template is known only after routing has run, which is why this is logged
            // on the way out. A request that matched nothing is recorded as "unmatched" rather
            // than with its raw path, which could carry an identifier or a query string.
            var routeTemplate = ProblemDetailsConfiguration.RouteTemplateOf(context);
            var correlationId = ProblemDetailsConfiguration.CorrelationIdOf(context);

            logger.LogInformation(
                MessageTemplate,
                context.Request.Method,
                routeTemplate,
                statusCode,
                (long)elapsed.TotalMilliseconds,
                correlationId);
        }
    }
}
