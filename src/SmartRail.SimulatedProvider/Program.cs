using SmartRail.SimulatedProvider.Clients;
using SmartRail.SimulatedProvider.Configuration;
using SmartRail.SimulatedProvider.Processing;
using SmartRail.SimulatedProvider.Startup;

var builder = WebApplication.CreateBuilder(args);

var options = SimulatedProviderOptions.FromEnvironment();

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(new JourneyStore(options.StationId));
builder.Services.AddSingleton(new HttpClient
{
    BaseAddress = options.BaseUrl,
    Timeout = TimeSpan.FromSeconds(30),
});
builder.Services.AddSingleton(provider =>
{
    var http = provider.GetRequiredService<HttpClient>();
    http.DefaultRequestHeaders.Add("X-Api-Key", options.ApiKey);

    return new SmartRailProviderClient(http);
});
builder.Services.AddHostedService<StartupLoader>();

var app = builder.Build();

// The kitchen screen is one static page that polls GET /state every two seconds.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.MapGet("/state", (JourneyStore store) => Results.Ok(store.Snapshot()));

app.MapPost("/webhook", (ProviderWebhookEvent payload, JourneyStore store) =>
{
    if (store.IsFailing)
    {
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }

    var outcome = store.Apply(payload);

    return Results.Ok(new { received = true, outcome });
});

// The failure switch: while it is on, every push answers 503 so SmartRail's retries can be seen.
app.MapPost("/admin/fail", (JourneyStore store) =>
    Results.Ok(new { failing = store.ToggleFailure() }));

app.Run();

/// <summary>The application's entry point, named for tests that want to host it.</summary>
public partial class Program;
