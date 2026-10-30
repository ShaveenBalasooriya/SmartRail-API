namespace SmartRail.Host.Http;

/// <summary>
/// Marks every response of the public read API as not cacheable (design D7, passenger-departures
/// spec "Passenger data not collected"). It is the one place the rule is applied for the whole
/// <c>/api/v1/public</c> group, so no handler repeats it.
/// </summary>
/// <remarks>
/// The header is set from <see cref="HttpResponse.OnStarting"/> rather than before the rest of the
/// pipeline runs, so a response the endpoint never produced still carries it: a validation failure
/// (400), an unknown station or journey (404) and the rate limiter's refusal (429) are all answered
/// downstream of this middleware.
/// </remarks>
public sealed class PublicResponseMiddleware(RequestDelegate next)
{
    /// <summary>The group whose responses are uncacheable; the same prefix the public endpoints map.</summary>
    public const string PublicPathPrefix = ApiRoutes.BasePath + "/public";

    /// <summary>The directive every public response carries: nothing about it may be stored.</summary>
    public const string NoStore = "no-store";

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Request.Path.StartsWithSegments(PublicPathPrefix))
        {
            await next(context);
            return;
        }

        context.Response.OnStarting(() =>
        {
            context.Response.Headers.CacheControl = NoStore;
            return Task.CompletedTask;
        });

        await next(context);
    }
}
