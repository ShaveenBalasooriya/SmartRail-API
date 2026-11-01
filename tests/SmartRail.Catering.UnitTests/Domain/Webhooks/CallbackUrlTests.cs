using SmartRail.Catering.Domain.Webhooks;

namespace SmartRail.Catering.UnitTests.Domain.Webhooks;

public sealed class CallbackUrlTests
{
    [Fact]
    public void Happy_path_trims_and_accepts_an_absolute_https_callback()
    {
        // Arrange
        const string value = "  https://provider.example.com/smartrail/events  ";

        // Act
        var result = CallbackUrl.Create(value);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("https://provider.example.com/smartrail/events", result.Value.Value);
    }

    [Fact]
    public void An_http_url_at_the_maximum_length_is_accepted()
    {
        // Arrange
        const string prefix = "http://provider.example.com/";
        var value = prefix + new string('a', CallbackUrl.MaxLength - prefix.Length);

        // Act
        var result = CallbackUrl.Create(value);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(CallbackUrl.MaxLength, result.Value.Value.Length);
    }

    [Fact]
    public void A_url_rebuilt_from_storage_has_value_equality()
    {
        // Arrange
        const string value = "https://provider.example.com/events";
        var original = CallbackUrl.Create(value).Value;

        // Act
        var restored = CallbackUrl.FromStorage(value);

        // Assert
        Assert.Equal(original, restored);
        Assert.Equal(value, restored.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_url_is_rejected(string? value)
    {
        // Arrange

        // Act
        var result = CallbackUrl.Create(value);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("webhook.callback_url.required", result.Error.Code);
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("/relative/events")]
    [InlineData("ftp://provider.example.com/events")]
    public void A_relative_url_or_unsupported_scheme_is_rejected(string value)
    {
        // Arrange

        // Act
        var result = CallbackUrl.Create(value);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("webhook.callback_url.invalid", result.Error.Code);
    }

    [Fact]
    public void A_url_longer_than_the_maximum_is_rejected()
    {
        // Arrange
        var value = "https://provider.example.com/" + new string('a', CallbackUrl.MaxLength);

        // Act
        var result = CallbackUrl.Create(value);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("webhook.callback_url.too_long", result.Error.Code);
    }

    [Fact]
    public void A_url_containing_credentials_is_rejected()
    {
        // Arrange
        const string value = "https://user:secret@provider.example.com/events";

        // Act
        var result = CallbackUrl.Create(value);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("webhook.callback_url.credentials", result.Error.Code);
    }
}
