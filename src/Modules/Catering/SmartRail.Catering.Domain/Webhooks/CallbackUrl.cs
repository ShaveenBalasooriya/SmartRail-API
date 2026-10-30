using SmartRail.SharedKernel.Primitives;
using SmartRail.SharedKernel.Results;

namespace SmartRail.Catering.Domain.Webhooks;

/// <summary>
/// The callback URL a provider registers. SmartRail calls an address the provider controls, so the
/// value is restricted at registration only: an absolute <c>http</c> or <c>https</c> URL of at most
/// 2,048 characters with no credentials (webhook-subscriptions spec, "URL restrictions"; design D4).
/// Blocking private and loopback addresses is deferred until a real provider is connected.
/// </summary>
public sealed class CallbackUrl : ValueObject
{
    /// <summary>The longest accepted URL, which is also the column's length.</summary>
    public const int MaxLength = 2048;

    private static readonly Error Required =
        Error.Validation("webhook.callback_url.required", "A callback URL is required.");

    private static readonly Error Invalid =
        Error.Validation(
            "webhook.callback_url.invalid",
            "A callback URL must be an absolute http or https URL.");

    private static readonly Error TooLong =
        Error.Validation(
            "webhook.callback_url.too_long",
            "A callback URL must be at most 2048 characters.");

    private static readonly Error Credentials =
        Error.Validation(
            "webhook.callback_url.credentials",
            "A callback URL must not include credentials.");

    private CallbackUrl(string value) => Value = value;

    /// <summary>The URL as it is stored and called.</summary>
    public string Value { get; }

    /// <summary>Creates a callback URL, rejecting anything the registration rule refuses.</summary>
    public static Result<CallbackUrl> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Failure<CallbackUrl>(Required);
        }

        var trimmed = value.Trim();

        if (trimmed.Length > MaxLength)
        {
            return Result.Failure<CallbackUrl>(TooLong);
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return Result.Failure<CallbackUrl>(Invalid);
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return Result.Failure<CallbackUrl>(Invalid);
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return Result.Failure<CallbackUrl>(Credentials);
        }

        return new CallbackUrl(trimmed);
    }

    /// <summary>
    /// Rebuilds a URL from the value EF Core materialises. The stored value was validated when it was
    /// written, so this does not validate again.
    /// </summary>
    public static CallbackUrl FromStorage(string value) => new(value);

    /// <inheritdoc />
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
