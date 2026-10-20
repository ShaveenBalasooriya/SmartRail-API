using SmartRail.Audit.Infrastructure;
using SmartRail.Catering.Infrastructure;
using SmartRail.Distribution.Infrastructure;
using SmartRail.Host.Health;
using SmartRail.Host.Http;
using SmartRail.Host.Persistence;
using SmartRail.IdentityAccess.Infrastructure;
using SmartRail.LiveOperations.Infrastructure;
using SmartRail.RailNetwork.Infrastructure;
using SmartRail.RailNetwork.Infrastructure.Endpoints;
using SmartRail.Audit.Infrastructure.Persistence;
using SmartRail.Catering.Infrastructure.Persistence;
using SmartRail.IdentityAccess.Infrastructure.Persistence;
using SmartRail.LiveOperations.Infrastructure.Persistence;
using SmartRail.RailNetwork.Infrastructure.Persistence;

namespace SmartRail.Host;

/// <summary>
/// The composition root. This is the only place that knows all six modules exist: it registers
/// their services, applies their migrations and maps their endpoints (design D1).
/// </summary>
public static class HostComposition
{
    /// <summary>
    /// Registers everything the Host needs. Modules register themselves through their own
    /// <c>Add&lt;Module&gt;Module</c> extension, so the Host never reaches into a module's internals.
    /// </summary>
    public static IServiceCollection AddSmartRailHost(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services.AddSingleton(TimeProvider.System);

        services.AddSmartRailProblemDetails();
        services.AddSmartRailCors(configuration);
        services.AddSmartRailOpenApi();

        // Records the matched route template on the request before the HTTP-method policy can
        // replace it with the pattern-less 405 rejection endpoint (diagnostic-logging spec).

        services.AddExceptionHandler<GlobalExceptionHandler>();


        // A request body that cannot be read becomes an exception the exception handler can
        // describe, so a rejected field is named in the 400 response instead of a bare status code.
        services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(
            options => options.ThrowOnBadRequest = true);

        services.AddMediatR(mediatr =>
        {
            mediatr.RegisterServicesFromAssemblies(ModuleAssemblies.ApplicationAssemblies());
        });

        services.AddIdentityAccessModule(configuration);
        services.AddRailNetworkModule(configuration);
        services.AddLiveOperationsModule(configuration);
        services.AddDistributionModule(configuration);
        services.AddCateringModule(configuration);
        services.AddAuditModule(configuration);

        services.AddModuleDatabases();
        services.AddSingleton<DatabaseMigrator>();

        services.AddSmartRailHealthChecks(configuration);

        return services;
    }

    /// <summary>Maps the health probes, the documentation and every module's endpoints.</summary>
    public static WebApplication MapSmartRail(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapSmartRailHealth();
        app.MapSmartRailDocumentation();
        app.MapModuleEndpoints();

        foreach (var registration in app.Services.GetServices<IEndpointRegistration>())
        {
            registration.Map(app);
        }

        return app;
    }

    /// <summary>
    /// Maps each module's endpoint group under <c>/api/v1</c>. The modules have no endpoints in
    /// the foundation change, so this establishes the convention they will all follow: one group
    /// per module area, created inside the module's own project.
    /// </summary>
    public static WebApplication MapModuleEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var api = app.MapApiV1();

        // Each module's group is created here so the base path and the tag are decided once. A
        // later change adds its endpoints to its own group inside its own Infrastructure project.
        api.MapGroup("/identity").WithTags("Identity");
        var network = api.MapGroup("/network").WithTags("Network");
        var timetable = api.MapGroup("/timetable").WithTags("Timetable");
        api.MapGroup("/operations").WithTags("Operations");
        api.MapGroup("/distribution").WithTags("Public");
        api.MapGroup("/catering").WithTags("Provider");
        api.MapGroup("/audit").WithTags("Audit");

        network.MapRailNetworkEndpoints(timetable);

        return app;
    }

    private static void AddModuleDatabases(this IServiceCollection services)
    {
        services.AddSingleton(new ModuleDatabase(IdentityAccessModule.ModuleName, IdentityDbContext.Schema, typeof(IdentityDbContext)));
        services.AddSingleton(new ModuleDatabase(RailNetworkModule.ModuleName, RailNetworkDbContext.Schema, typeof(RailNetworkDbContext)));
        services.AddSingleton(new ModuleDatabase(LiveOperationsModule.ModuleName, LiveOperationsDbContext.Schema, typeof(LiveOperationsDbContext)));
        services.AddSingleton(new ModuleDatabase(CateringModule.ModuleName, CateringDbContext.Schema, typeof(CateringDbContext)));
        services.AddSingleton(new ModuleDatabase(AuditModule.ModuleName, AuditDbContext.Schema, typeof(AuditDbContext)));
    }

    private static void AddSmartRailHealthChecks(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException(
                "Connection string 'Database' is not configured. Set ConnectionStrings__Database.");

        services.AddSingleton(_ => Npgsql.NpgsqlDataSource.Create(connectionString));

        services
            .AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>(
                HealthChecks.DatabaseCheckName,
                failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy,
                tags: [HealthChecks.ReadyTag])
            .AddCheck<MigrationsAppliedHealthCheck>(
                HealthChecks.MigrationsCheckName,
                failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy,
                tags: [HealthChecks.ReadyTag]);

        // Liveness deliberately has no dependency: it answers 200 while the process can respond,
        // even when PostgreSQL is down (service-health spec).
        services.AddHealthChecks().AddCheck(
            "self",
            () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(),
            tags: [HealthChecks.LiveTag]);
    }

    /// <summary>The module assemblies MediatR scans for notification handlers.</summary>
    private static class ModuleAssemblies
    {
        public static System.Reflection.Assembly[] ApplicationAssemblies() =>
        [
            typeof(IdentityAccess.Infrastructure.IdentityAccessModule).Assembly,
            typeof(RailNetwork.Infrastructure.RailNetworkModule).Assembly,
            typeof(LiveOperations.Infrastructure.LiveOperationsModule).Assembly,
            typeof(Distribution.Infrastructure.DistributionModule).Assembly,
            typeof(Catering.Infrastructure.CateringModule).Assembly,
            typeof(Audit.Infrastructure.AuditModule).Assembly,
        ];
    }
}
