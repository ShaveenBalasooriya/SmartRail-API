using SmartRail.SharedKernel.Application.Messaging;

namespace SmartRail.Catering.UnitTests.TestDoubles;

/// <summary>
/// Creates the module's internal handlers through their public CQRS interfaces. This keeps each test
/// on one handler without changing production visibility or starting MediatR and its pipeline.
/// </summary>
internal static class HandlerDriver
{
    internal static ICommandHandler<TCommand, TResponse> CreateCommandHandler<TCommand, TResponse>(
        params object[] dependencies)
        where TCommand : ICommand<TResponse> =>
        Create<ICommandHandler<TCommand, TResponse>, TCommand>(dependencies);

    internal static ICommandHandler<TCommand> CreateCommandHandler<TCommand>(
        params object[] dependencies)
        where TCommand : ICommand =>
        Create<ICommandHandler<TCommand>, TCommand>(dependencies);

    internal static IQueryHandler<TQuery, TResponse> CreateQueryHandler<TQuery, TResponse>(
        params object[] dependencies)
        where TQuery : IQuery<TResponse> =>
        Create<IQueryHandler<TQuery, TResponse>, TQuery>(dependencies);

    private static TInterface Create<TInterface, TRequest>(object[] dependencies)
    {
        var handlerType = typeof(TRequest).Assembly.GetTypes().Single(type =>
            !type.IsAbstract && typeof(TInterface).IsAssignableFrom(type));

        return (TInterface)Activator.CreateInstance(handlerType, dependencies)!;
    }
}
