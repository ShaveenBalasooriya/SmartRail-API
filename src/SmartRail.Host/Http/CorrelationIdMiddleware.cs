using Microsoft.Extensions.Primitives;

namespace SmartRail.Host.Http;

/// <summary>
/// Assigns, validates and echoes the correlation ID for a request, and pushes it into the logging
/// scope so every entry written while the request runs carries it (api-conventions spec).
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    /// <summary>The request and response header name.</summary>
    public const string HeaderName = "X-Correlation-Id";

    /// <summary>The logging scope key the ID is published under.</summary>
    public const string LogScopeKey = "CorrelationId";

    /// <summary>The longest accepted caller-supplied ID.</summary>
    public const int MaxLength = 64;

    public async Task InvokeAsync(HttpContext context, ILogger<CorrelationIdMiddleware> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(logger);

        var correlationId = Resolve(context.Request.Headers[HeaderName]);
        context.TraceIdentifier = correlationId;
        context.Items[LogScopeKey] = correlationId;

        // Set on the way in so the header is present even on a response written by the server
        // itself (a 404 from routing, a 500 from the exception handler).
        context.Response.Headers[HeaderName] = correlationId;

        using (logger.BeginScope(new Dictionary<string, object> { [LogScopeKey] = correlationId }))
        {
            await next(context);
        }
    }

    /// <summary>
    /// Echoes a well-formed caller ID; otherwise generates one. "Well-formed" is up to 64
    /// characters of letters, digits and hyphens, which cannot carry a header injection.
    /// </summary>
    public static string Resolve(StringValues supplied)
    {
        if (supplied.Count == 1 && IsWellFormed(supplied[0]))
        {
            return supplied[0]!;
        }

        return Guid.NewGuid().ToString("D");
    }

    /// <summary>True when the value is a non-empty, well-formed correlation ID.</summary>
    public static bool IsWellFormed(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaxLength)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character != '-')
            {
                return false;
            }
        }

        return true;
    }
}
