namespace SmartRail.SimulatedProvider.Configuration;

/// <summary>
/// What the stand-in provider needs to talk to SmartRail: its base URL, its API key, the callback URL
/// it registers and the station whose journeys its kitchen serves (simulated-provider spec,
/// "Registers and loads state"; design D6).
/// </summary>
public sealed record SimulatedProviderOptions(
    Uri BaseUrl,
    string ApiKey,
    string CallbackUrl,
    Guid StationId)
{
    /// <summary>The variable naming SmartRail's base URL, for example <c>http://api:8080</c>.</summary>
    public const string BaseUrlVariable = "SMARTRAIL_BASE_URL";

    /// <summary>The variable naming the provider API key SmartRail issued to this provider.</summary>
    public const string ApiKeyVariable = "SMARTRAIL_API_KEY";

    /// <summary>The variable naming the callback URL this provider registers.</summary>
    public const string CallbackUrlVariable = "SIMULATED_PROVIDER_CALLBACK_URL";

    /// <summary>The variable naming the station whose journeys the kitchen screen shows.</summary>
    public const string StationVariable = "SIMULATED_PROVIDER_STATION_ID";

    /// <summary>Colombo Fort, the seeded interchange, when no station is configured.</summary>
    public static readonly Guid DefaultStationId = Guid.Parse("04624d0f-9410-72e3-967f-501ed71f2441");

    /// <summary>Reads the options from the environment, refusing to start without the SmartRail details.</summary>
    public static SimulatedProviderOptions FromEnvironment()
    {
        var baseUrl = Required(BaseUrlVariable);

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"{BaseUrlVariable} must be an absolute URL.");
        }

        var station = Environment.GetEnvironmentVariable(StationVariable);

        return new SimulatedProviderOptions(
            uri,
            Required(ApiKeyVariable),
            Required(CallbackUrlVariable),
            string.IsNullOrWhiteSpace(station) ? DefaultStationId : Guid.Parse(station));
    }

    private static string Required(string variable)
    {
        var value = Environment.GetEnvironmentVariable(variable);

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{variable} is required.");
        }

        return value;
    }
}
