using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SmartRail.SharedKernel.Infrastructure.Persistence;

/// <summary>
/// Registers the request transaction and the hook a module's <c>AddDbContext</c> attaches to enlist
/// its context in it (design D1, D3).
/// </summary>
public static class RequestTransactionExtensions
{
    /// <summary>
    /// Registers the scoped request transaction. The Host calls this once, from the same connection
    /// string every module context uses.
    /// </summary>
    public static IServiceCollection AddRequestTransaction(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException(
                "Connection string 'Database' is not configured. Set ConnectionStrings__Database.");

        return services.AddRequestTransaction(connectionString);
    }

    /// <summary>Registers the scoped request transaction for a known connection string.</summary>
    public static IServiceCollection AddRequestTransaction(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddScoped(_ => new RequestTransaction(connectionString));
        services.AddScoped<IRequestTransaction>(provider => provider.GetRequiredService<RequestTransaction>());
        services.AddScoped(provider => new RequestTransactionEnlistmentInterceptor(
            provider.GetRequiredService<RequestTransaction>()));

        return services;
    }

    /// <summary>
    /// Enlists this context in the request transaction before it saves, when the request has one.
    /// Use it on every module context, in the same place as <c>AddOutboxSignal</c>:
    /// <c>options.UseNpgsql(...).AddRequestTransactionEnlistment(provider)</c>.
    /// </summary>
    public static DbContextOptionsBuilder AddRequestTransactionEnlistment(
        this DbContextOptionsBuilder options,
        IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(services);

        return options.AddInterceptors(services.GetRequiredService<RequestTransactionEnlistmentInterceptor>());
    }
}
