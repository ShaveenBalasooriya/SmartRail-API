using System.Net;

namespace SmartRail.Host.IntegrationTests.Flows.Support;

/// <summary>
/// The flow suite's assertions. Every one of them names the behaviour that broke, because a flow test
/// that fails with a bare status code says nothing about which rule was violated. xUnit's own
/// overloads take a message on only a few of the asserts the flows need, so the rest wrap them here.
/// </summary>
internal static class Verify
{
    /// <summary>The problem-details media type every refusal on the staff surface carries.</summary>
    public const string ProblemJson = "application/problem+json";

    /// <summary>The two values are equal, or the behaviour named in <paramref name="behaviour"/> broke.</summary>
    public static void Equal<T>(T expected, T actual, string behaviour)
    {
        Assert.True(
            EqualityComparer<T>.Default.Equals(expected, actual),
            $"{behaviour}, but was '{Describe(actual)}' instead of '{Describe(expected)}'.");
    }

    /// <summary>The condition holds, or the behaviour named in <paramref name="behaviour"/> broke.</summary>
    public static void True(bool condition, string behaviour) => Assert.True(condition, $"{behaviour}.");

    /// <summary>The collection is empty, or the behaviour named in <paramref name="behaviour"/> broke.</summary>
    public static void Empty<T>(IReadOnlyCollection<T> items, string behaviour) =>
        Assert.True(
            items.Count == 0,
            $"{behaviour}, but {items.Count} row(s) were there: "
            + $"[{string.Join(" ; ", items)}]");

    /// <summary>A scenario write should be accepted with 201 and carry the change it published.</summary>
    public static void Created<T>(ApiAttempt<T> attempt, string behaviour)
        where T : class
    {
        Assert.True(
            attempt.Status == HttpStatusCode.Created,
            $"{behaviour} with 201, but it answered {(int)attempt.Status} {attempt.Status}. Body: {attempt.Body}");
        Assert.True(
            attempt.Value is not null,
            $"{behaviour} with 201 and the published change, but the body could not be read as one. "
            + $"Body: {attempt.Body}");
    }

    /// <summary>A write by the wrong role should be refused with 403 problem details.</summary>
    public static void Refused<T>(ApiAttempt<T> attempt, string behaviour)
        where T : class
    {
        Assert.True(
            attempt.Status == HttpStatusCode.Forbidden,
            $"{behaviour} with 403, but it answered {(int)attempt.Status} {attempt.Status}. Body: {attempt.Body}");
        Assert.True(
            string.Equals(attempt.MediaType, ProblemJson, StringComparison.Ordinal),
            $"{behaviour} with 403 problem details, but the media type was "
            + $"'{attempt.MediaType ?? "(none)"}'. Body: {attempt.Body}");
    }

    /// <summary>An attempt without a usable token should be refused with 401 problem details.</summary>
    public static void Unauthorized<T>(ApiAttempt<T> attempt, string behaviour)
        where T : class
    {
        Assert.True(
            attempt.Status == HttpStatusCode.Unauthorized,
            $"{behaviour} with 401, but it answered {(int)attempt.Status} {attempt.Status}. Body: {attempt.Body}");
        Assert.True(
            string.Equals(attempt.MediaType, ProblemJson, StringComparison.Ordinal),
            $"{behaviour} with 401 problem details, but the media type was "
            + $"'{attempt.MediaType ?? "(none)"}'. Body: {attempt.Body}");
    }

    /// <summary>A write the rules refuse as malformed or impossible should answer 400 problem details.</summary>
    public static void BadRequest<T>(ApiAttempt<T> attempt, string behaviour)
        where T : class =>
        Rejected(attempt, HttpStatusCode.BadRequest, behaviour);

    /// <summary>A write the state of the journey refuses should answer 409 problem details.</summary>
    public static void Conflict<T>(ApiAttempt<T> attempt, string behaviour)
        where T : class =>
        Rejected(attempt, HttpStatusCode.Conflict, behaviour);

    /// <summary>A write that names nothing the record holds should answer 404 problem details.</summary>
    public static void NotFound<T>(ApiAttempt<T> attempt, string behaviour)
        where T : class =>
        Rejected(attempt, HttpStatusCode.NotFound, behaviour);

    /// <summary>A refused write with the status the rule gives it, always as problem details.</summary>
    private static void Rejected<T>(ApiAttempt<T> attempt, HttpStatusCode expected, string behaviour)
        where T : class
    {
        Assert.True(
            attempt.Status == expected,
            $"{behaviour} with {(int)expected} {expected}, but it answered "
            + $"{(int)attempt.Status} {attempt.Status}. Body: {attempt.Body}");
        Assert.True(
            string.Equals(attempt.MediaType, ProblemJson, StringComparison.Ordinal),
            $"{behaviour} with {(int)expected} problem details, but the media type was "
            + $"'{attempt.MediaType ?? "(none)"}'. Body: {attempt.Body}");
    }

    /// <summary>A read should succeed with 200 and carry its typed body.</summary>
    public static void Ok<T>(ApiAttempt<T> attempt, string behaviour)
        where T : class
    {
        Assert.True(
            attempt.Status == HttpStatusCode.OK,
            $"{behaviour} with 200, but it answered {(int)attempt.Status} {attempt.Status}. Body: {attempt.Body}");
        Assert.True(
            attempt.Value is not null,
            $"{behaviour} with 200 and a readable body, but the body could not be read. Body: {attempt.Body}");
    }

    /// <summary>
    /// The two renderings of the same state are identical. Used where a refusal has to leave something
    /// alone: the before and after are printed so the difference is visible in the failure.
    /// </summary>
    public static void Unchanged(string before, string after, string behaviour)
    {
        Assert.True(
            string.Equals(before, after, StringComparison.Ordinal),
            $"{behaviour}, but it changed.{Environment.NewLine}"
            + $"Before: {before}{Environment.NewLine}After: {after}");
    }

    /// <summary>One value in a message, so a failure never prints a type name instead of the value.</summary>
    public static string Describe(object? value) => value switch
    {
        null => "(null)",
        string text => text,
        _ => value.ToString() ?? "(null)",
    };
}
