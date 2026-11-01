using System.Reflection;
using SmartRail.SharedKernel.Application.Messaging;

namespace SmartRail.IdentityAccess.UnitTests.TestDoubles;

/// <summary>Creates internal implementations through their public interfaces without a production change.</summary>
internal static class HandlerDriver
{
    internal static IQueryHandler<TQuery, TResponse> CreateQueryHandler<TQuery, TResponse>(
        params object[] dependencies)
        where TQuery : IQuery<TResponse> =>
        Create<IQueryHandler<TQuery, TResponse>>(typeof(TQuery).Assembly, dependencies);

    internal static TInterface CreateService<TInterface>(Assembly implementationAssembly, params object[] dependencies) =>
        Create<TInterface>(implementationAssembly, dependencies);

    private static TInterface Create<TInterface>(Assembly assembly, object[] dependencies)
    {
        var implementationType = assembly.GetTypes().Single(type =>
            !type.IsAbstract && typeof(TInterface).IsAssignableFrom(type));

        return (TInterface)Activator.CreateInstance(implementationType, dependencies)!;
    }
}
