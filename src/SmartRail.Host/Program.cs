using SmartRail.Host;
using SmartRail.Host.Http;
using SmartRail.Host.Persistence;

var builder = WebApplication.CreateBuilder(args);

// .NET 10 built-in Minimal API validation (design D4). It is enabled here rather than in the
// composition root because its source generator intercepts this call site in the entry point.
builder.Services.AddValidation();

builder.Services.AddSmartRailHost(builder.Configuration, builder.Environment);

// The JSON rules are configured on the HTTP layer, not per endpoint, so every endpoint and every
// error response uses the same names, enum strings and timestamp format (api-conventions spec).
builder.Services.ConfigureHttpJsonOptions(options => ApiJson.Configure(options.SerializerOptions));

// Logs are one JSON object per line, which is what the Compose stack collects and what the PII
// test reads (design D5, diagnostic-logging spec).
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.UseUtcTimestamp = true;
    options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
    options.JsonWriterOptions = new System.Text.Json.JsonWriterOptions { Indented = false };
});

var app = builder.Build();

// Correlation first: everything after it, including the exception handler and the request log,
// reads the ID it assigned.
app.UseMiddleware<CorrelationIdMiddleware>();

app.UseExceptionHandler();
app.UseSmartRailStatusCodePages();

app.UseCors(CorsConfiguration.PolicyName);

app.UseMiddleware<RequestLoggingMiddleware>();

await app.Services.GetRequiredService<DatabaseMigrator>().MigrateAsync(app.Lifetime.ApplicationStopping);

app.MapSmartRail();

app.Run();

/// <summary>
/// The entry point's type, so the integration tests can start the real Host with
/// <c>WebApplicationFactory&lt;Program&gt;</c>.
/// </summary>
public partial class Program
{
    protected Program()
    {
    }
}
