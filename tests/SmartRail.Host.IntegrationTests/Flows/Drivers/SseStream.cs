using System.Globalization;
using System.Net;
using System.Text;
using SmartRail.Host.Http;
using SmartRail.Host.IntegrationTests.Flows.Support;
using SmartRail.Host.IntegrationTests.Infrastructure;

namespace SmartRail.Host.IntegrationTests.Flows.Drivers;

/// <summary>One event frame a flow read from a stream.</summary>
/// <param name="Id">The stream position the frame carries, or null for a frame that has none (the reset).</param>
/// <param name="EventType">The frame's event type, for example <c>delay_published</c>.</param>
/// <param name="Data">The frame's data as it arrived, the same live view the journey detail returns.</param>
internal sealed record SseFrame(string? Id, string EventType, string Data)
{
    /// <summary>The position as the stream sent it, for a resume header or an order comparison.</summary>
    public long? Position => Id is not null && long.TryParse(Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var position)
        ? position
        : null;
}

/// <summary>What one window of reading a stream produced.</summary>
/// <param name="Events">The event frames that arrived.</param>
/// <param name="Comments">How many comment frames arrived, the heartbeat a quiet stream still sends.</param>
internal sealed record SseRead(IReadOnlyList<SseFrame> Events, int Comments);

/// <summary>
/// One open Server-Sent Events response, read frame by frame. Both live channels speak the same frame
/// format, so one reader serves the passenger stream and the Guard's: the only differences are the
/// route and the credential, which live here rather than in a test.
/// </summary>
/// <remarks>
/// A read never throws when a stream is quiet: it returns what arrived inside the window, and the
/// caller asserts on that, so a stream that never sends fails the flow's assertion rather than
/// hanging the test.
/// </remarks>
internal sealed class SseStream : IDisposable
{
    private const string PublicStreamRoute = $"{ApiRoutes.BasePath}/public/stream";
    private const string GuardStreamRoute = $"{ApiRoutes.BasePath}/guard/stream";

    private readonly HttpResponseMessage _response;
    private readonly StreamReader _reader;

    private SseStream(HttpResponseMessage response, StreamReader reader)
    {
        _response = response;
        _reader = reader;
    }

    /// <summary>
    /// Opens the public stream, optionally filtered to one journey and optionally resuming after a
    /// position. A resume sends the standard <c>Last-Event-ID</c> header, which is the only way a
    /// position is passed.
    /// </summary>
    public static Task<SseStream> OpenPublicAsync(
        HttpClient client,
        Guid? journeyId = null,
        string? lastEventId = null) =>
        OpenAsync(
            client,
            journeyId is { } id ? $"{PublicStreamRoute}?journey={id}" : PublicStreamRoute,
            token: null,
            lastEventId);

    /// <summary>Opens the Guard's stream as <paramref name="session"/>, optionally resuming after a position.</summary>
    public static Task<SseStream> OpenGuardAsync(
        HttpClient client,
        StaffSession? session,
        string? lastEventId = null) =>
        OpenAsync(client, GuardStreamRoute, session?.Token, lastEventId);

    /// <summary>
    /// Reads frames until the window elapses, until the first event, or until the expected number of
    /// events arrived. A quiet stream is not an error here.
    /// </summary>
    public async Task<SseRead> ReadAsync(
        TimeSpan window,
        bool stopAtFirstEvent,
        int? stopAfterEvents = null)
    {
        using var timeout = new CancellationTokenSource(window);

        var events = new List<SseFrame>();
        var comments = 0;
        string? id = null;
        string? eventType = null;
        var data = new StringBuilder();

        try
        {
            while (true)
            {
                var line = await _reader.ReadLineAsync(timeout.Token);

                if (line is null)
                {
                    break;
                }

                if (line.StartsWith(':'))
                {
                    comments++;
                    continue;
                }

                if (line.Length == 0)
                {
                    if (eventType is not null)
                    {
                        events.Add(new SseFrame(id, eventType, data.ToString()));

                        if (stopAtFirstEvent || (stopAfterEvents is { } until && events.Count >= until))
                        {
                            break;
                        }
                    }

                    id = null;
                    eventType = null;
                    data.Clear();
                    continue;
                }

                if (line.StartsWith("id: ", StringComparison.Ordinal))
                {
                    id = line[4..];
                }
                else if (line.StartsWith("event: ", StringComparison.Ordinal))
                {
                    eventType = line[7..];
                }
                else if (line.StartsWith("data: ", StringComparison.Ordinal))
                {
                    data.Append(line[6..]);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The window elapsed, or the stream was closed under the reader.
        }

        return new SseRead(events, comments);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _reader.Dispose();
        _response.Dispose();
    }

    /// <summary>
    /// Opens the route and checks the conventions every stream response shares: 200 as an event
    /// stream, uncacheable, and no cookie.
    /// </summary>
    private static async Task<SseStream> OpenAsync(
        HttpClient client,
        string route,
        string? token,
        string? lastEventId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, route);

        if (token is not null)
        {
            request.WithStaffToken(token);
        }

        if (lastEventId is not null)
        {
            request.Headers.TryAddWithoutValidation("Last-Event-ID", lastEventId);
        }

        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        Verify.Equal(
            HttpStatusCode.OK,
            response.StatusCode,
            "A stream subscription should open with 200");
        Verify.Equal(
            "text/event-stream",
            response.Content.Headers.ContentType?.MediaType,
            "A stream subscription should answer as an event stream");
        Verify.Equal(
            "no-store",
            response.Headers.CacheControl?.ToString(),
            "A stream subscription should be uncacheable");
        Verify.True(
            !response.Headers.TryGetValues("Set-Cookie", out _),
            "A stream subscription should set no cookie");

        return new SseStream(
            response,
            new StreamReader(await response.Content.ReadAsStreamAsync(), Encoding.UTF8));
    }
}
