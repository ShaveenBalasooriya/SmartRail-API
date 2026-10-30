using System.Net;

namespace SmartRail.Host.IntegrationTests.Flows.Support;

/// <summary>
/// One HTTP attempt a flow made, with the status, the media type, the raw body and the typed body when
/// the call was expected to carry one. The body is kept even for a refusal, so a failing assertion can
/// say what the server actually answered rather than only its status code.
/// </summary>
/// <typeparam name="T">The typed body the call returns when it succeeds.</typeparam>
/// <param name="Status">The status code the server answered with.</param>
/// <param name="MediaType">The response's media type, for example <c>application/problem+json</c>.</param>
/// <param name="Body">The response body as it arrived.</param>
/// <param name="Value">The typed body, or <see langword="null"/> when the call did not succeed.</param>
internal sealed record ApiAttempt<T>(HttpStatusCode Status, string? MediaType, string Body, T? Value)
    where T : class;
