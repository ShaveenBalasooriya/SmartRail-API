using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartRail.SharedKernel.Application.Events;
using SmartRail.SharedKernel.Infrastructure.Events;
using SmartRail.SharedKernel.Infrastructure.Persistence;

namespace SmartRail.SharedKernel.Infrastructure;

/// <summary>
/// The event bus's registration point (design D1, D3, D8). A publishing module registers its
/// publisher and its outbox source here; the Host registers the bus itself; the Host never
/// constructs any of it directly.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers the publisher for the module whose <typeparamref name="TContext"/> owns the outbox,
    /// under <paramref name="key"/> from <see cref="IntegrationEventPublisherKeys"/>. A handler
    /// resolves it with <c>[FromKeyedServices(key)]</c>.
    /// </summary>
    public static IServiceCollection AddIntegrationEventPublisher<TContext>(
        this IServiceCollection services,
        string key)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        services.AddKeyedScoped<IIntegrationEventPublisher>(
            key,
            (provider, _) => new OutboxIntegrationEventPublisher(
                provider.GetRequiredService<TContext>(),
                provider.GetRequiredService<TimeProvider>()));

        return services;
    }

    /// <summary>
    /// Registers the module whose <typeparamref name="TContext"/> owns the outbox as a dispatcher
    /// source, plus its in-process transport, under <paramref name="key"/> from
    /// <see cref="IntegrationEventPublisherKeys"/>. The module must also attach
    /// <see cref="OutboxSignalExtensions.AddOutboxSignal"/> to that context.
    /// </summary>
    public static IServiceCollection AddOutboxSource<TContext>(
        this IServiceCollection services,
        string key)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        services.AddSingleton<IOutboxSource>(new OutboxSource<TContext>(key));

        services.AddKeyedScoped<IEventTransport>(
            key,
            (provider, _) => new PostgresEventTransport(
                provider.GetRequiredService<TContext>(),
                key,
                provider.GetRequiredService<EventConsumerRegistry>(),
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<ILogger<PostgresEventTransport>>()));

        return services;
    }

    /// <summary>
    /// Registers one consumer of one event type (design D4). The consumer's <c>Name</c> is the
    /// transport's stable identity for it and is stored with every delivery.
    /// </summary>
    public static IServiceCollection AddEventConsumer<TEvent, TConsumer>(this IServiceCollection services)
        where TEvent : notnull
        where TConsumer : class, IEventConsumer<TEvent>
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<TConsumer>();

        // The event type's simple name is the outbox row's event_type (design D2).
        services.AddSingleton(new EventConsumerRegistration(
            typeof(TEvent).Name,
            provider => new EventConsumerHandle<TEvent>(provider.GetRequiredService<TConsumer>())));

        return services;
    }

    /// <summary>
    /// Registers the dispatcher, its wake-up signal and the consumer registry (design D3, D7). The
    /// Host calls this once; with no outbox source registered the dispatcher simply does nothing.
    /// </summary>
    public static IServiceCollection AddEventBus(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<OutboxSignal>();
        services.AddSingleton<OutboxSignalInterceptor>();
        services.AddSingleton<EventConsumerRegistry>();
        services.AddHostedService<EventDispatcher>();

        return services;
    }
}
