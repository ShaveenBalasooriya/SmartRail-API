using System.Reflection;
using Carter;
using FluentValidation;
using SmartRail.Audit.Api;
using SmartRail.Audit.Infrastructure;
using SmartRail.Catering.Api;
using SmartRail.Catering.Infrastructure;
using SmartRail.Distribution.Api;
using SmartRail.Distribution.Infrastructure;
using SmartRail.Host.Health;
using SmartRail.Host.Http;
using SmartRail.Host.Persistence;
using SmartRail.IdentityAccess.Api;
using SmartRail.IdentityAccess.Infrastructure;
using SmartRail.LiveOperations.Api;
using SmartRail.LiveOperations.Infrastructure;
using SmartRail.RailNetwork.Api;
using SmartRail.RailNetwork.Infrastructure;
using SmartRail.SharedKernel.Application.Behaviors;
using SmartRail.SharedKernel.Infrastructure;
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
    /// Registers everything the Host needs. Each module registers its own layers through its
    /// <c>Add&lt;Module&gt;Application</c>, <c>Add&lt;Module&gt;Infrastructure</c> and
    /// <c>Add&lt;Module&gt;Api</c> extensions, so the Host never reaches into a module's internals.
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

        // The event bus's dispatcher and wake-up signal. Each publishing module adds its own
        // outbox source, so this registers the bus once and then has no module knowledge.
        services.AddEventBus();

        services.AddSmartRailProblemDetails();
        services.AddSmartRailCors(configuration);
        services.AddSmartRailOpenApi(configuration, environment);

        // Records the matched route template on the request before the HTTP-method policy can
        // replace it with the pattern-less 405 rejection endpoint (diagnostic-logging spec).

        services.AddExceptionHandler<GlobalExceptionHandler>();


        // A request body that cannot be read becomes an exception the exception handler can
        // describe, so a rejected field is named in the 400 response instead of a bare status code.
        services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(
            options => options.ThrowOnBadRequest = true);

        // One registration over every module's Application assembly, where commands, queries,
        // validators and notification handlers now all live.
        services.AddMediatR(mediatr =>
        {
            mediatr.RegisterServicesFromAssemblies(ModuleAssemblies.ApplicationAssemblies());

            // The order is the order a request meets them: logging wraps validation (design D2).
            mediatr.AddOpenBehavior(typeof(LoggingPipelineBehavior<,>));
            mediatr.AddOpenBehavior(typeof(ValidationPipelineBehavior<,>));
        });

        services.AddValidatorsFromAssemblies(ModuleAssemblies.ApplicationAssemblies());

        // Carter discovers the Api assemblies explicitly: relying on the entry assembly's
        // dependency context would also scan assemblies the Host does not map.
        services.AddCarter(new DependencyContextAssemblyCatalog(ModuleAssemblies.ApiAssemblies()));

        services.AddIdentityAccessInfrastructure(configuration);
        services.AddRailNetworkInfrastructure(configuration);
        services.AddLiveOperationsInfrastructure(configuration);
        services.AddDistributionInfrastructure(configuration);
        services.AddCateringInfrastructure(configuration);
        services.AddAuditInfrastructure(configuration);

        services.AddIdentityAccessApi(configuration, environment);
        services.AddRailNetworkApi();
        services.AddLiveOperationsApi();
        services.AddDistributionApi();
        services.AddCateringApi();
        services.AddAuditApi();

        services.AddModuleDatabases();
        services.AddSingleton<DatabaseMigrator>();
        services.AddSingleton<DatabaseSeeder>();

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
    /// Maps each module's endpoints under <c>/api/v1</c>. Each module owns the Carter modules in
    /// its Api project; the base path is decided once, here.
    /// </summary>
    public static WebApplication MapModuleEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var api = app.MapApiV1();

        api.MapGroup("/identity").WithTags("Identity");
        api.MapGroup("/operations").WithTags("Operations");
        api.MapGroup("/distribution").WithTags("Public");
        api.MapGroup("/catering").WithTags("Provider");
        api.MapGroup("/audit").WithTags("Audit");

        // Every module's Carter modules are mapped through the same /api/v1 group. Each module's
        // modules declare their own area group and tags.
        api.MapCarter();

        return app;
    }

    private static void AddModuleDatabases(this IServiceCollection services)
    {
        services.AddSingleton(new ModuleDatabase(IdentityDbContext.Schema, IdentityDbContext.Schema, typeof(IdentityDbContext)));
        services.AddSingleton(new ModuleDatabase(RailNetworkDbContext.Schema, RailNetworkDbContext.Schema, typeof(RailNetworkDbContext)));
        services.AddSingleton(new ModuleDatabase(LiveOperationsDbContext.Schema, LiveOperationsDbContext.Schema, typeof(LiveOperationsDbContext)));
        services.AddSingleton(new ModuleDatabase(CateringDbContext.Schema, CateringDbContext.Schema, typeof(CateringDbContext)));
        services.AddSingleton(new ModuleDatabase(AuditDbContext.Schema, AuditDbContext.Schema, typeof(AuditDbContext)));
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
                tags: [HealthChecks.ReadyTag])
            .AddCheck<SeedingCompletedHealthCheck>(
                HealthChecks.SeedingCheckName,
                failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy,
                tags: [HealthChecks.ReadyTag]);

        // Liveness deliberately has no dependency: it answers 200 while the process can respond,
        // even when PostgreSQL is down (service-health spec).
        services.AddHealthChecks().AddCheck(
            "self",
            () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(),
            tags: [HealthChecks.LiveTag]);
    }

    /// <summary>The module assemblies the Host scans once, for MediatR, FluentValidation and Carter.</summary>
    private static class ModuleAssemblies
    {
        /// <summary>Every module's Application assembly, where commands, queries and validators live.</summary>
        public static Assembly[] ApplicationAssemblies() =>
        [
            typeof(IdentityAccess.Application.DependencyInjection).Assembly,
            typeof(RailNetwork.Application.DependencyInjection).Assembly,
            typeof(LiveOperations.Application.DependencyInjection).Assembly,
            typeof(Distribution.Application.DependencyInjection).Assembly,
            typeof(Catering.Application.DependencyInjection).Assembly,
            typeof(Audit.Application.DependencyInjection).Assembly,
        ];

        /// <summary>Every module's Api assembly, which holds its Carter modules.</summary>
        public static Assembly[] ApiAssemblies() =>
        [
            typeof(IdentityAccess.Api.DependencyInjection).Assembly,
            typeof(RailNetwork.Api.DependencyInjection).Assembly,
            typeof(LiveOperations.Api.DependencyInjection).Assembly,
            typeof(Distribution.Api.DependencyInjection).Assembly,
            typeof(Catering.Api.DependencyInjection).Assembly,
            typeof(Audit.Api.DependencyInjection).Assembly,
        ];
    }
}
