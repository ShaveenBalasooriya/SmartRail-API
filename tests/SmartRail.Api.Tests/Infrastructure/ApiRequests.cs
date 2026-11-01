using System.Net.Http.Json;

namespace SmartRail.Api.Tests.Infrastructure;

/// <summary>
/// How one request authenticates: nothing, a staff bearer token, or a provider API key. Keeping it
/// explicit is what lets an access test say "the Guard's token, on the controller's route" without
/// touching a header itself.
/// </summary>
/// <param name="Header">The header the credential travels in, when there is one.</param>
/// <param name="Value">The header's value, or <see langword="null"/> for no credential at all.</param>
public readonly record struct Credential(string? Header, string? Value)
{
    /// <summary>No credentials at all: an anonymous request.</summary>
    public static Credential None => default;

    /// <summary>A staff bearer token.</summary>
    public static Credential Staff(string? token) =>
        new("Authorization", token is null ? null : $"Bearer {token}");

    /// <summary>A catering provider's API key.</summary>
    public static Credential Provider(string? apiKey) => new("X-Api-Key", apiKey);
}

/// <summary>The HTTP calls every test makes, in one place so a test reads as its request and its assertions.</summary>
internal static class ApiRequests
{
    /// <summary>The correlation header the API assigns, echoes and repeats in every problem detail.</summary>
    public const string CorrelationHeader = "X-Correlation-Id";

    /// <summary>Sends one request with the given credential and, when it has one, a JSON body.</summary>
    public static async Task<HttpResponseMessage> SendAsync(
        this ApiTarget api,
        HttpMethod method,
        string path,
        Credential credential,
        object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);

        if (credential.Value is not null)
        {
            request.Headers.TryAddWithoutValidation(credential.Header!, credential.Value);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await api.Client.SendAsync(request);
    }

    /// <summary>Reads one resource.</summary>
    public static Task<HttpResponseMessage> GetAsync(this ApiTarget api, string path, Credential credential) =>
        api.SendAsync(HttpMethod.Get, path, credential);

    /// <summary>Writes a new resource.</summary>
    public static Task<HttpResponseMessage> PostAsync(
        this ApiTarget api,
        string path,
        Credential credential,
        object? body = null) =>
        api.SendAsync(HttpMethod.Post, path, credential, body);

    /// <summary>Replaces a resource.</summary>
    public static Task<HttpResponseMessage> PutAsync(
        this ApiTarget api,
        string path,
        Credential credential,
        object? body = null) =>
        api.SendAsync(HttpMethod.Put, path, credential, body);

    /// <summary>Removes a resource.</summary>
    public static Task<HttpResponseMessage> DeleteAsync(this ApiTarget api, string path, Credential credential) =>
        api.SendAsync(HttpMethod.Delete, path, credential);

    /// <summary>Reads the response body as one of the records in <see cref="ApiModels"/>.</summary>
    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response)
        where T : class
    {
        var body = await response.Content.ReadFromJsonAsync<T>();

        Assert.NotNull(body);
        return body;
    }
}
